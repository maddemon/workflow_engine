using System.Text;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Http;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// HTTP 节点（<see cref="HttpRequestNode"/> 与 <see cref="HttpToolNode"/>）共用的执行流程。
/// 负责 URL 解析、SSRF 校验、认证注入、Headers/Body 脚本求值与发送。
/// </summary>
internal static class HttpNodeExecution
{
    /// <summary>
    /// 执行 HTTP 请求并返回节点结果。
    /// </summary>
    public static async Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        Script url,
        HttpMethodOption method,
        HttpRequestAuthMode authentication,
        string? credentialId,
        bool sendHeaders,
        Script? headersExpression,
        bool sendBody,
        Script? bodyExpression,
        CancellationToken cancellationToken)
    {
        try
        {
            if (url is null || string.IsNullOrWhiteSpace(url.Source))
            {
                return context.ErrorResult("MissingUrl", "URL is required.");
            }

            var resolvedUrl = url.GetResult<string>();
            if (string.IsNullOrWhiteSpace(resolvedUrl))
            {
                return context.ErrorResult("MissingUrl", "URL resolution failed.");
            }

            if (SsrfGuard.IsInternalTarget(resolvedUrl))
            {
                return context.ErrorResult("SsrfBlocked", "Target URL points to a blocked internal/loopback address.");
            }

            var methodStr = method.ToString().ToUpperInvariant();
            using var request = new HttpRequestMessage(new HttpMethod(methodStr), resolvedUrl);

            if (authentication != HttpRequestAuthMode.None && !string.IsNullOrEmpty(credentialId))
            {
                var credentialValue = await context.ResolveApiKeyAsync(credentialId, cancellationToken).ConfigureAwait(false);
                if (credentialValue is not null)
                {
                    ApplyAuthentication(request, authentication, credentialValue);
                }
            }

            var scriptCache = context.GetScriptCache();
            var scriptContext = ScriptContext.From(context);

            var preparedHeaders = sendHeaders && headersExpression is not null && !string.IsNullOrWhiteSpace(headersExpression.Source)
                ? scriptCache.GetOrPrepare(headersExpression)
                : null;
            var preparedBody = sendBody && bodyExpression is not null && !string.IsNullOrWhiteSpace(bodyExpression.Source) &&
                method is HttpMethodOption.Post or HttpMethodOption.Put or HttpMethodOption.Patch
                ? scriptCache.GetOrPrepare(bodyExpression)
                : null;

            if (preparedHeaders is not null || preparedBody is not null)
            {
                // 引擎随 using 释放，避免每次请求泄漏 Jint 引擎（设计 §4.4）。
                using var engine = JsEngine.Create();
                using var session = (preparedHeaders ?? preparedBody)!.CreateSession(engine);

                if (preparedHeaders is not null)
                {
                    var headersResult = await session.RunAsync(preparedHeaders, scriptContext, cancellationToken).ConfigureAwait(false);
                    var headers = headersResult.To<Dictionary<string, string>>();
                    if (headers is not null)
                    {
                        foreach (var (key, value) in headers)
                        {
                            request.Headers.TryAddWithoutValidation(key, value);
                        }
                    }
                }

                if (preparedBody is not null)
                {
                    var bodyResult = await session.RunAsync(preparedBody, scriptContext, cancellationToken).ConfigureAwait(false);
                    var bodyJson = bodyResult.ToJson()?.ToJsonString() ?? string.Empty;
                    request.Content = new StringContent(bodyJson, Encoding.UTF8, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"));
                }
            }

            var client = context.HttpClientPool?.GetClient();
            if (client is null)
            {
                return context.ErrorResult("HttpClientUnavailable", "HTTP client pool is not configured.");
            }

            return await HttpExecutionHelper.SendAndBuildResultAsync(client, request, context.Node.Id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return context.ErrorResult("Cancelled", "HTTP request was cancelled.");
        }
        catch (HttpRequestException ex)
        {
            return context.ErrorResult("HttpRequestFailed", $"HTTP request failed: {ex.Message}");
        }
        catch (ScriptErrorException ex)
        {
            return context.ErrorResult("ScriptError", $"Script evaluation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return context.ErrorResult("UnexpectedError", $"Unexpected HTTP error: {ex.Message}");
        }
    }

    private static void ApplyAuthentication(HttpRequestMessage request, HttpRequestAuthMode authentication, string credentialValue)
    {
        switch (authentication)
        {
            case HttpRequestAuthMode.BearerToken:
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {credentialValue}");
                break;
            case HttpRequestAuthMode.ApiKey:
                request.Headers.TryAddWithoutValidation("X-API-Key", credentialValue);
                break;
            case HttpRequestAuthMode.BasicAuth:
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentialValue));
                request.Headers.TryAddWithoutValidation("Authorization", $"Basic {base64}");
                break;
        }
    }
}
