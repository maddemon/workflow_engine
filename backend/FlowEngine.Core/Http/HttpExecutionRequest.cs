using System.Net;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Http;

/// <summary>
/// HTTP 执行请求参数。
/// </summary>
public sealed class HttpExecutionRequest
{
    /// <summary>请求 URL。</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>HTTP 方法。</summary>
    public HttpMethod Method { get; init; } = HttpMethod.Get;

    /// <summary>认证模式。</summary>
    public HttpRequestAuthMode AuthMode { get; init; } = HttpRequestAuthMode.None;

    /// <summary>凭据 ID。</summary>
    public string? CredentialId { get; init; }

    /// <summary>查询参数名称（QueryParameter 认证模式使用）。</summary>
    public string? QueryParameterName { get; init; }

    /// <summary>自定义请求头。</summary>
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>请求体内容。</summary>
    public string? BodyContent { get; init; }

    /// <summary>业务成功条件表达式。</summary>
    public Script? SuccessWhen { get; init; }
}
