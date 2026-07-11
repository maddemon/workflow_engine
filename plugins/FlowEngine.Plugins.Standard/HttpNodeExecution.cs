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
        Script? successWhen = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (url is null || string.IsNullOrWhiteSpace(url.Source))
            {
                return context.ErrorResult("MissingUrl", "URL is required.");
            }

            var resolvedUrl = await url.EvaluateAsync<string>(context, cancellationToken: cancellationToken);
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

            if (sendHeaders && headersExpression is not null && !string.IsNullOrWhiteSpace(headersExpression.Source))
            {
                var headers = await headersExpression.EvaluateAsync<Dictionary<string, string>>(context, cancellationToken: cancellationToken);
                if (headers is not null)
                {
                    foreach (var (key, value) in headers)
                    {
                        request.Headers.TryAddWithoutValidation(key, value);
                    }
                }
            }

            if (sendBody && bodyExpression is not null && !string.IsNullOrWhiteSpace(bodyExpression.Source) &&
                method is HttpMethodOption.Post or HttpMethodOption.Put or HttpMethodOption.Patch)
            {
                var bodyJson = (await bodyExpression.EvaluateAsync<JsonNode>(context, cancellationToken: cancellationToken))?.ToJsonString() ?? string.Empty;
                request.Content = new StringContent(bodyJson, Encoding.UTF8, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"));
            }

            var client = context.HttpClientPool?.GetClient();
            if (client is null)
            {
                return context.ErrorResult("HttpClientUnavailable", "HTTP client pool is not configured.");
            }

            var result = await HttpExecutionHelper.SendAndBuildResultAsync(client, request, context.Node.Id, cancellationToken)
                .ConfigureAwait(false);

            // 阶段零 0.2：HTTP 成功后再判 successWhen 业务成功表达式（如钉钉 errcode != 0 但 HTTP 200）
            if (result.Success && successWhen is not null && !string.IsNullOrWhiteSpace(successWhen.Source))
            {
                // 防御性 NRE 保护：当 result.Output 为 null 时（理论上不会发生，但避免空引用），使用 ?.Items 安全访问。
                var items = result.Output?.Items;
                var envelope = items is not null && items.Count > 0 ? items[0].Data as JsonObject : null;
                var body = envelope?["body"];
                var statusCode = envelope?["statusCode"]?.GetValue<int>() ?? 200;
                var statusText = envelope?["statusText"]?.GetValue<string>();
                var businessOk = await HttpExecutionHelper.EvaluateSuccessWhenAsync(
                    successWhen, body, statusCode, statusText, context, cancellationToken).ConfigureAwait(false);
                if (!businessOk)
                {
                    return context.ErrorResult("SuccessWhenFailed", $"successWhen 表达式判定为失败：{successWhen.Source}");
                }
            }

            return result;
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
