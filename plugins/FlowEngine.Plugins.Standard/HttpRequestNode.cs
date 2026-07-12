using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// HTTP 请求节点，支持静态配置和占位符。
/// </summary>
public sealed class HttpRequestNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "httpRequest";

    /// <inheritdoc />
    AiNodeDefinition? INodeType.GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "HTTP Request", "Core", false,
            "发起 HTTP 请求并解析响应。支持 GET/POST/PUT/DELETE/PATCH，可配置认证（Bearer/Basic/API Key/Query）、自定义请求头与请求体。返回状态码、响应头与自动解析的响应体。常用于调用外部 API、Webhook 回调。",
            ["http", "api", "rest"],
            JsonNode.Parse("""{"type":"object","properties":{"statusCode":{"type":"number"},"headers":{"type":"object"},"body":{"description":"响应体，已按 Content-Type 自动解析为 JSON/文本"}}}"""),
            AiDefinitionHelpers.Example("GET 请求示例",
                JsonNode.Parse("""{"method":"GET","url":"https://api.example.com/users"}"""),
                JsonNode.Parse("""{"statusCode":200,"body":[{"id":1,"name":"Alice"}]}""")));

    /// <inheritdoc />
    public string DisplayName => "HTTP Request";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "globe";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// HTTP 请求方法。
    /// </summary>
    [Description("HTTP request method.")]
    public HttpMethodOption Method { get; set; } = HttpMethodOption.Get;

    /// <summary>
    /// 目标 URL，支持 JS 表达式（如 <c>"https://api.example.com/" + $json.path</c>）。
    /// </summary>
    [DisplayName("URL")]
    [Description("Target URL. Use JS expression to build URL dynamically (e.g. 'https://api.com/' + $json.path).")]
    [Hint(PresentationHint.Expression)]
    public Script Url { get; set; } = Script.Empty;

    /// <summary>
    /// 认证方式。
    /// </summary>
    [Description("Authentication method.")]
    public HttpRequestAuthMode Authentication { get; set; } = HttpRequestAuthMode.None;

    /// <summary>
    /// 凭据 ID（用于 Bearer Token、API Key 或 QueryParameter）。
    /// </summary>
    [Credential(FlowConstants.CredentialFields.ApiKey)]
    [Description("Credential ID for authentication.")]
    public string? CredentialId { get; set; }

    /// <summary>
    /// QueryParameter 认证模式下的查询参数名（如 access_token）。默认 access_token。
    /// </summary>
    [Description("Query parameter name for QueryParameter auth mode (default: access_token).")]
    public string QueryParameterName { get; set; } = "access_token";

    /// <summary>
    /// 是否发送自定义请求头。
    /// </summary>
    [DisplayName("Send Headers")]
    [Description("Whether to send custom headers.")]
    public bool SendHeaders { get; set; } = false;

    /// <summary>
    /// 请求头，JS 脚本，返回对象。
    /// </summary>
    [DisplayName("Headers")]
    [Description("Headers script. Must return an object. Example: { 'Authorization': 'Bearer ' + $json.token }")]
    [Hint(PresentationHint.Script)]
    [DisplayCondition(nameof(SendHeaders), true)]
    public Script? HeadersExpression { get; set; }

    /// <summary>
    /// 是否发送请求体（仅 POST/PUT/PATCH 时显示）。
    /// </summary>
    [DisplayName("Send Body")]
    [Description("Whether to send a request body.")]
    [DisplayCondition(nameof(Method), HttpMethodOption.Post)]
    [DisplayCondition(nameof(Method), HttpMethodOption.Put)]
    [DisplayCondition(nameof(Method), HttpMethodOption.Patch)]
    public bool SendBody { get; set; } = false;

    /// <summary>
    /// 请求体，JS 脚本，返回对象。
    /// </summary>
    [DisplayName("Body")]
    [Description("Body script. Must return an object. Example: { name: $json.name, count: $json.count }")]
    [Hint(PresentationHint.Script)]
    [DisplayCondition(nameof(SendBody), true)]
    public Script? BodyExpression { get; set; }

    /// <summary>
    /// 业务成功判定表达式。配置后，即使 HTTP 返回 2xx，仍须该表达式为真（如 <c>$json.errcode == 0</c>），
    /// 否则节点判定为失败；未配置时仅按 HTTP 状态码判定（向后兼容）。
    /// </summary>
    [DisplayName("Success When")]
    [Description("Business success condition. When set, even a 2xx HTTP response fails the node if this expression evaluates to false (e.g. '$json.errcode == 0').")]
    [Hint(PresentationHint.Expression)]
    public Script SuccessWhen { get; set; } = Script.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        return HttpNodeExecution.ExecuteAsync(
            context,
            Url,
            Method,
            Authentication,
            CredentialId,
            QueryParameterName,
            SendHeaders,
            HeadersExpression,
            SendBody,
            BodyExpression,
            SuccessWhen,
            cancellationToken);
    }
}

// HttpPlaceholder is defined in HttpToolNode.cs
