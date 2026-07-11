using System.ComponentModel;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// HTTP 工具节点，作为 Agent 的工具被调用。
/// 支持静态配置（method、URL、authentication）和占位符机制。
/// </summary>
public sealed class HttpToolNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "httpTool";

    /// <inheritdoc />
    public string DisplayName => "HTTP Tool";

    /// <inheritdoc />
    public string Category => "AI";

    /// <inheritdoc />
    public string Icon => "globe";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// HTTP 方法。
    /// </summary>
    [Description("HTTP request method.")]
    public HttpMethodOption Method { get; set; } = HttpMethodOption.Get;

    /// <summary>
    /// 目标 URL，JS 表达式，返回字符串。
    /// </summary>
    [DisplayName("URL")]
    [Description("URL expression. Must return a string. Example: 'https://api.com/' + $json.path")]
    [Hint(PresentationHint.Expression)]
    public Script Url { get; set; } = Script.Empty;

    /// <summary>
    /// 认证方式。
    /// </summary>
    [Description("Authentication method.")]
    public HttpRequestAuthMode Authentication { get; set; } = HttpRequestAuthMode.None;

    /// <summary>
    /// 凭据 ID。
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

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Tools, DisplayName = "Tool Output", Direction = PortDirection.Output, Type = PortType.AgentTool }
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
            null,
            cancellationToken);
    }
}

/// <summary>
/// HTTP 占位符定义。
/// </summary>
public sealed class HttpPlaceholder
{
    /// <summary>
    /// 占位符名称（对应 URL/Body 中的 {name}）。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 占位符描述（帮助 LLM 理解需要什么值）。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 是否必填。
    /// </summary>
    public bool Required { get; set; } = true;
}
