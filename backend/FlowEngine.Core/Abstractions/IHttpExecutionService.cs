using System.Net;
using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// HTTP 执行请求参数。
/// </summary>
public class HttpExecutionRequest
{
    /// <summary>
    /// 请求 URL（已解析完的字符串）。
    /// </summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// HTTP 方法。
    /// </summary>
    public HttpMethod Method { get; init; } = HttpMethod.Get;

    /// <summary>
    /// 认证方式。
    /// </summary>
    public HttpRequestAuthMode AuthMode { get; init; }

    /// <summary>
    /// 凭据 ID 或名称。
    /// </summary>
    public string? CredentialId { get; init; }

    /// <summary>
    /// QueryParameter 模式下的查询参数名。
    /// </summary>
    public string? QueryParameterName { get; init; }

    /// <summary>
    /// 自定义请求头。
    /// </summary>
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// 请求体内容（已序列化的 JSON 字符串）。
    /// </summary>
    public string? BodyContent { get; init; }

    /// <summary>
    /// 业务成功条件表达式（可选）。
    /// </summary>
    public Script? SuccessWhen { get; init; }
}

/// <summary>
/// HTTP 执行服务，封装客户端池获取、SSRF 预检、认证注入、请求发送与统一错误处理。
/// </summary>
public interface IHttpExecutionService
{
    /// <summary>
    /// 执行 HTTP 请求并返回节点结果。
    /// </summary>
    Task<NodeExecutionResult> ExecuteAsync(
        HttpExecutionRequest request,
        NodeExecutionContext context,
        CancellationToken cancellationToken);
}
