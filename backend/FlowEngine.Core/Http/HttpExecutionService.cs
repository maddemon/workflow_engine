using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Http;

/// <summary>
/// HTTP 执行服务实现，统一处理客户端池获取、SSRF 预检、认证注入、请求发送与异常映射。
/// 同时实现 <see cref="FlowEngine.Core.Abstractions.IHttpExecutionService"/>，供节点经抽象接口调用。
/// </summary>
public class HttpExecutionService : FlowEngine.Core.Abstractions.IHttpExecutionService
{
    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(
        HttpExecutionRequest request,
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Validate URL
            if (string.IsNullOrWhiteSpace(request.Url))
            {
                return context.ErrorResult(FlowConstants.ErrorCodes.MissingUrl, "URL is required.");
            }

            // 2. SSRF guard
            var ssrfGuard = context.GuardSsrf(request.Url);
            if (ssrfGuard is not null) return ssrfGuard;

            // 3. Get HTTP client from pool
            var client = context.HttpClientPool?.GetClient();
            if (client is null)
            {
                return context.ErrorResult(FlowConstants.ErrorCodes.HttpClientUnavailable,
                    "HTTP client pool is not configured.");
            }

            // 4. Build request message
            using var httpRequest = new HttpRequestMessage(request.Method, request.Url);

            // 5. Apply authentication from credential
            if (request.AuthMode != HttpRequestAuthMode.None && !string.IsNullOrEmpty(request.CredentialId))
            {
                var credential = await context.ResolveCredentialAsync(request.CredentialId, cancellationToken)
                    .ConfigureAwait(false);
                if (credential is null)
                {
                    return new NodeExecutionResult
                    {
                        Success = false,
                        Output = new DataBatch
                        {
                            Items =
                            [
                                new DataItem
                                {
                                    Success = false,
                                    SourceIndex = 0,
                                    Error = new NodeError
                                    {
                                        Code = "CredentialNotFound",
                                        Message = $"凭据 '{request.CredentialId}' 不存在。请先通过凭证管理创建该凭据。",
                                    },
                                },
                            ],
                        },
                    };
                }
                ApplyAuth(httpRequest, request.AuthMode, credential, request.QueryParameterName);
            }

            // 6. Apply custom headers
            if (request.Headers is { Count: > 0 })
            {
                foreach (var (key, value) in request.Headers)
                {
                    httpRequest.Headers.TryAddWithoutValidation(key, value);
                }
            }

            // 7. Set request body for mutating methods
            if (!string.IsNullOrEmpty(request.BodyContent) &&
                (request.Method == HttpMethod.Post ||
                 request.Method == HttpMethod.Put ||
                 request.Method == HttpMethod.Patch))
            {
                httpRequest.Content = new StringContent(request.BodyContent, Encoding.UTF8,
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"));
            }

            // 8. Send and parse response
            var result = await HttpExecutionHelper.SendAndBuildResultAsync(
                client, httpRequest, context.Node.Id, cancellationToken)
                .ConfigureAwait(false);

            // 9. Evaluate business success condition (successWhen expression)
            if (result.Success && request.SuccessWhen is not null &&
                !string.IsNullOrWhiteSpace(request.SuccessWhen.Source))
            {
                var items = result.Output?.Items;
                var envelope = items is not null && items.Count > 0
                    ? items[0].Data as JsonObject
                    : null;
                var body = envelope?["body"];
                var statusCode = envelope?["statusCode"]?.GetValue<int>() ?? 200;
                var statusText = envelope?["statusText"]?.GetValue<string>();
                var businessOk = await HttpExecutionHelper.EvaluateSuccessWhenAsync(
                    request.SuccessWhen, body, statusCode, statusText,
                    context, cancellationToken).ConfigureAwait(false);
                if (!businessOk)
                {
                    var errcode = body?["errcode"]?.GetValue<int>();
                    var errmsg = body?["errmsg"]?.GetValue<string>();
                    var subMsg = body?["sub_msg"]?.GetValue<string>();
                    var detail = errcode.HasValue ? $", errcode={errcode}" : "";
                    if (!string.IsNullOrEmpty(subMsg))
                        detail += $", {subMsg}";
                    else if (!string.IsNullOrEmpty(errmsg))
                        detail += $", {errmsg}";
                    return context.ErrorResult(FlowConstants.ErrorCodes.SuccessWhenFailed,
                        $"Business condition not met: {request.SuccessWhen.Source}{detail}");
                }
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.Cancelled,
                "HTTP request was cancelled.");
        }
        catch (HttpRequestException ex)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.HttpRequestFailed,
                $"HTTP request failed: {ex.Message}");
        }
        catch (ScriptErrorException ex)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.ScriptError,
                $"Script evaluation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.UnexpectedError,
                $"Unexpected HTTP error: {ex.Message}");
        }
    }

    /// <summary>
    /// 根据认证模式从凭据中提取字段值并注入请求。
    /// 合并 HttpNodeExecution.ApplyAuthentication 与 PaginateNode.ApplyAuthHeaderAsync 的逻辑：
    /// Bearer 优先尝试 accessToken → token → apiKey；
    /// ApiKey 读取 ApiKey 字段；
    /// BasicAuth 读取 username + password。
    /// </summary>
    private static void ApplyAuth(
        HttpRequestMessage request,
        HttpRequestAuthMode mode,
        CredentialValue credential,
        string? queryParameterName)
    {
        var fields = credential.Fields;

        switch (mode)
        {
            case HttpRequestAuthMode.BearerToken:
            {
                if (fields.TryGetValue("accessToken", out var token) ||
                    fields.TryGetValue("token", out token) ||
                    fields.TryGetValue(FlowConstants.CredentialFields.ApiKey, out token))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
                }
                break;
            }
            case HttpRequestAuthMode.ApiKey:
            {
                if (fields.TryGetValue(FlowConstants.CredentialFields.ApiKey, out var apiKey))
                {
                    request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
                }
                break;
            }
            case HttpRequestAuthMode.BasicAuth:
            {
                var user = (fields.TryGetValue("username", out var u) ? u
                    : fields.TryGetValue("user", out u) ? u
                    : string.Empty) ?? string.Empty;
                var pass = (fields.TryGetValue("password", out var p) ? p
                    : string.Empty) ?? string.Empty;
                var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
                request.Headers.TryAddWithoutValidation("Authorization", $"Basic {basic}");
                break;
            }
            case HttpRequestAuthMode.QueryParameter:
            {
                // 将凭据值作为 URL 查询参数附加
                if (!string.IsNullOrEmpty(queryParameterName) && request.RequestUri is not null)
                {
                    // 尝试多个字段获取值
                    string? credentialValue = null;
                    if (fields.TryGetValue("accessToken", out var t)) credentialValue = t;
                    else if (fields.TryGetValue("token", out t)) credentialValue = t;
                    else if (fields.TryGetValue(FlowConstants.CredentialFields.ApiKey, out t)) credentialValue = t;

                    if (!string.IsNullOrEmpty(credentialValue))
                    {
                        var separator = request.RequestUri.Query.Length == 0 ? '?' : '&';
                        var encodedName = Uri.EscapeDataString(queryParameterName);
                        var encodedValue = Uri.EscapeDataString(credentialValue);
                        var newUri = $"{request.RequestUri.GetLeftPart(UriPartial.Path)}" +
                                     $"{request.RequestUri.Query}" +
                                     $"{separator}{encodedName}={encodedValue}" +
                                     $"{request.RequestUri.Fragment}";
                        request.RequestUri = new Uri(newUri);
                    }
                }
                break;
            }
        }
    }
}
