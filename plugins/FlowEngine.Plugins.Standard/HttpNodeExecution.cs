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
/// 负责 URL/Headers/Body 脚本求值，将已解析的参数交给 <see cref="IHttpExecutionService"/> 执行。
/// </summary>
internal static class HttpNodeExecution
{
    private static readonly IHttpExecutionService HttpService = new HttpExecutionService();

    /// <summary>
    /// 执行 HTTP 请求并返回节点结果。
    /// </summary>
    public static async Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        Script url,
        HttpMethodOption method,
        HttpRequestAuthMode authentication,
        string? credentialId,
        string queryParameterName,
        bool sendHeaders,
        Script? headersExpression,
        bool sendBody,
        Script? bodyExpression,
        Script? successWhen = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. URL 校验与求值
            if (url is null || string.IsNullOrWhiteSpace(url.Source))
            {
                return context.ErrorResult(FlowConstants.ErrorCodes.MissingUrl, "URL is required.");
            }

            var resolvedUrl = await url.EvaluateAsync<string>(context, cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(resolvedUrl))
            {
                return context.ErrorResult(FlowConstants.ErrorCodes.MissingUrl, "URL resolution failed.");
            }

            // 2. 求值请求头（脚本表达式 → Dictionary）
            Dictionary<string, string>? headers = null;
            if (sendHeaders && headersExpression is not null && !string.IsNullOrWhiteSpace(headersExpression.Source))
            {
                headers = await headersExpression.EvaluateAsync<Dictionary<string, string>>(context, cancellationToken: cancellationToken);
            }

            // 3. 求值请求体（脚本表达式 → JSON 字符串）
            string? bodyContent = null;
            if (sendBody && bodyExpression is not null && !string.IsNullOrWhiteSpace(bodyExpression.Source) &&
                method is HttpMethodOption.Post or HttpMethodOption.Put or HttpMethodOption.Patch)
            {
                var bodyJson = (await bodyExpression.EvaluateAsync<JsonNode>(context, cancellationToken: cancellationToken))?.ToJsonString() ?? string.Empty;
                bodyContent = bodyJson;
            }

            // 4. 构建请求参数并委托给 IHttpExecutionService
            var httpRequest = new HttpExecutionRequest
            {
                Url = resolvedUrl,
                Method = new HttpMethod(method.ToString().ToUpperInvariant()),
                AuthMode = authentication,
                CredentialId = credentialId,
                QueryParameterName = queryParameterName,
                Headers = headers,
                BodyContent = bodyContent,
                SuccessWhen = successWhen
            };

            return await HttpService.ExecuteAsync(httpRequest, context, cancellationToken).ConfigureAwait(false);
        }
        catch (ScriptErrorException ex)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.ScriptError, $"Script evaluation failed: {ex.Message}");
        }
    }
}
