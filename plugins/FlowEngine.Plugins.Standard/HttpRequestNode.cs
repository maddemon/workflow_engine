using System.ComponentModel;
using System.Net.Http;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Http;
using FlowEngine.Core.Metadata;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// HTTP 请求节点，支持静态配置和占位符。
/// 新写法继承 <see cref="NodeBase"/>，通过 [NodeMeta]/[Port]/[Hint] 声明式描述元信息与参数；
/// URL/Headers/Body 经 <see cref="NodeBase.EvaluateContextAsync{T}"/> 在整批作用域求值，
/// 业务失败统一抛 <see cref="NodeExecutionException"/>（不再使用 context.ErrorResult），
/// 真正的 HTTP 收发与凭据解析委派给注入的 <see cref="IHttpExecutionService"/>（取代经 context 直接依赖）。
/// </summary>
[NodeMeta(TypeName = "httpRequest", DisplayName = "HTTP Request", Category = NodeCategory.Network, Icon = "globe")]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output)]
public sealed class HttpRequestNode : NodeBase
{
    /// <inheritdoc />
    protected override AiNodeDefinition? GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "HTTP Request", "Core", false,
            "发起 HTTP 请求并解析响应。支持 GET/POST/PUT/DELETE/PATCH，可配置认证（Bearer/Basic/API Key/Query）、自定义请求头与请求体。返回状态码、响应头与自动解析的响应体。常用于调用外部 API、Webhook 回调。",
            ["http", "api", "rest"],
            JsonNode.Parse("""{"type":"object","properties":{"statusCode":{"type":"number"},"headers":{"type":"object"},"body":{"description":"响应体，已按 Content-Type 自动解析为 JSON/文本"}}}"""),
            AiDefinitionHelpers.Example("GET 请求示例",
                JsonNode.Parse("""{"method":"GET","url":"https://api.example.com/users"}"""),
                JsonNode.Parse("""{"statusCode":200,"body":[{"id":1,"name":"Alice"}]}""")));

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
    [Credential(FlowConstants.CredentialFields.ApiKey, "oauth2")]
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
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        // 1. URL 校验与求值
        if (Url is null || string.IsNullOrWhiteSpace(Url.Source))
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.MissingUrl, "URL is required.");
        }

        var resolvedUrl = await EvaluateContextAsync<string>(Url, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(resolvedUrl))
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.MissingUrl, "URL resolution failed.");
        }

        // 2. 求值请求头（脚本表达式 → Dictionary）
        Dictionary<string, string>? headers = null;
        if (SendHeaders && HeadersExpression is not null && !string.IsNullOrWhiteSpace(HeadersExpression.Source))
        {
            headers = await EvaluateContextAsync<Dictionary<string, string>>(HeadersExpression, ct).ConfigureAwait(false);
        }

        // 3. 求值请求体（脚本表达式 → JSON 字符串）
        string? bodyContent = null;
        if (SendBody && BodyExpression is not null && !string.IsNullOrWhiteSpace(BodyExpression.Source) &&
            Method is HttpMethodOption.Post or HttpMethodOption.Put or HttpMethodOption.Patch)
        {
            var bodyJson = (await EvaluateContextAsync<JsonNode>(BodyExpression, ct).ConfigureAwait(false))?.ToJsonString() ?? string.Empty;
            bodyContent = bodyJson;
        }

        // 4. 构建请求参数并委派给注入的 IHttpExecutionService（内部处理客户端池、SSRF 预检、凭据解析、成功条件判定）
        if (HttpExecutionService is null)
        {
            throw new NodeExecutionException("HttpServiceUnavailable", "HTTP execution service is not available.");
        }

        var httpRequest = new HttpExecutionRequest
        {
            Url = resolvedUrl,
            Method = new HttpMethod(Method.ToString().ToUpperInvariant()),
            AuthMode = Authentication,
            CredentialId = CredentialId,
            QueryParameterName = QueryParameterName,
            Headers = headers,
            BodyContent = bodyContent,
            SuccessWhen = SuccessWhen
        };

        var result = await HttpExecutionService.ExecuteAsync(httpRequest, ExecutionContext, ct).ConfigureAwait(false);

        // 5. 业务失败统一转换为 NodeExecutionException，由框架适配层映射为失败结果（保持错误码/消息一致）
        if (!result.Success)
        {
            throw new NodeExecutionException(result.Error?.Code ?? FlowConstants.ErrorCodes.UnexpectedError, result.Error?.Message ?? "HTTP request failed.");
        }

        return NodeHandlerOutput.Data(result.Output);
    }
}
