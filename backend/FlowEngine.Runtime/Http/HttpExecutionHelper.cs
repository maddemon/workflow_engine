using System.Net;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;

namespace FlowEngine.Runtime.Http;

/// <summary>
/// HTTP 节点共享的请求执行与响应解析逻辑，消除 HttpRequestNode 与 HttpToolNode 之间的重复代码。
/// </summary>
public static class HttpExecutionHelper
{
    private static readonly HttpClient SharedClient = new();

    /// <summary>
    /// 发送已构建好的 HTTP 请求，并将响应解析为 <see cref="NodeExecutionResult"/>。
    /// </summary>
    /// <param name="request">已填充完毕的 HTTP 请求消息。</param>
    /// <param name="nodeDefinitionId">节点定义 ID，用于构建错误对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<NodeExecutionResult> SendAndBuildResultAsync(
        HttpRequestMessage request,
        Guid nodeDefinitionId,
        CancellationToken cancellationToken)
    {
        using var response = await SharedClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
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
