using System.Net;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Http;

/// <summary>
/// HTTP 节点共享的请求执行与响应解析逻辑，消除 HttpRequestNode 与 HttpToolNode 之间的重复代码。
/// </summary>
public static class HttpExecutionHelper
{
    /// <summary>
    /// 发送已构建好的 HTTP 请求，并将响应解析为 <see cref="NodeExecutionResult"/>。
    /// </summary>
    /// <param name="client">HTTP 客户端实例（由连接池提供）。</param>
    /// <param name="request">已填充完毕的 HTTP 请求消息。</param>
    /// <param name="nodeDefinitionId">节点定义 ID，用于构建错误对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<NodeExecutionResult> SendAndBuildResultAsync(
        HttpClient client,
        HttpRequestMessage request,
        Guid nodeDefinitionId,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var isSuccess = response.StatusCode < HttpStatusCode.BadRequest;

        var output = new JsonObject
        {
            ["statusCode"] = (int)response.StatusCode,
            ["statusText"] = response.StatusCode.ToString(),
            ["body"] = TryParseJson(responseBody, out var jsonNode) ? jsonNode : responseBody,
            ["headers"] = SerializeResponseHeaders(response)
        };

        return new NodeExecutionResult
        {
            Success = isSuccess,
            Output = new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = output,
                        Success = isSuccess,
                        SourceIndex = 0
                    }
                ]
            },
            Error = isSuccess
                ? null
                : new NodeError
                {
                    Code = "HttpError",
                    Message = $"HTTP request failed: {response.StatusCode}",
                    NodeDefinitionId = nodeDefinitionId,
                    Details = new Dictionary<string, string>
                    {
                        ["statusCode"] = ((int)response.StatusCode).ToString()
                    }
                }
        };
    }

    /// <summary>
    /// 判定 <c>successWhen</c> 业务成功表达式。
    /// <list type="bullet">
    ///   <item>未配置表达式时直接返回 <c>true</c>（向后兼容，仅按 HTTP 状态码判定）。</item>
    ///   <item>已配置时，在 HTTP 成功（2xx）前提下求值，表达式为真才视为业务成功。</item>
    ///   <item>求值异常或结果为假均返回 <c>false</c>。</item>
    /// </list>
    /// 表达式可访问 <c>$json</c>（响应体）、<c>$statusCode</c>、<c>$statusText</c> 三个全局变量。
    /// </summary>
    /// <param name="successWhen">业务成功表达式；为 null 或空源码时跳过判定。</param>
    /// <param name="responseBody">HTTP 响应体（作为 <c>$json</c> 注入）。</param>
    /// <param name="statusCode">HTTP 状态码（作为 <c>$statusCode</c> 注入）。</param>
    /// <param name="statusText">HTTP 状态文本（作为 <c>$statusText</c> 注入）。</param>
    /// <param name="context">节点执行上下文，用于脚本引擎求值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<bool> EvaluateSuccessWhenAsync(
        Script? successWhen,
        JsonNode? responseBody,
        int statusCode,
        string? statusText,
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (successWhen is null || string.IsNullOrWhiteSpace(successWhen.Source))
        {
            return true;
        }

        try
        {
            var script = new Script
            {
                Source = successWhen.Source,
                Language = ScriptLanguage.JavaScript,
                ReturnType = ScriptReturnType.Bool
            };

            return await script.EvaluateAsync<bool>(
                context,
                cancellationToken,
                ("$json", responseBody),
                ("$statusCode", statusCode),
                ("$statusText", statusText)).ConfigureAwait(false);
        }
        catch (ScriptErrorException)
        {
            return false;
        }
        catch (Exception)
        {
            // 表达式求值失败视为业务未满足，交由调用方标记节点失败
            return false;
        }
    }

    private static bool TryParseJson(string json, out JsonNode? node)
    {
        try
        {
            node = JsonNode.Parse(json);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            node = null;
            return false;
        }
    }

    private static JsonObject SerializeResponseHeaders(HttpResponseMessage response)
    {
        var headers = new JsonObject();
        foreach (var (key, values) in response.Headers)
        {
            headers[key] = string.Join(", ", values);
        }

        foreach (var (key, values) in response.Content.Headers)
        {
            headers[key] = string.Join(", ", values);
        }

        return headers;
    }
}
