using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Http;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// HTTP 工具节点，作为 Agent 的工具被调用。
/// 支持静态配置（method、URL、authentication）和占位符机制。
/// 参考 n8n 的 ToolHttpRequest 设计。
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
    /// URL 模板，支持 {placeholder} 语法。
    /// </summary>
    [Description("Target URL. Use {placeholder} for dynamic values that LLM will fill.")]
    public string Url { get; set; } = string.Empty;

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
    /// 是否发送自定义请求头。
    /// </summary>
    [Description("Whether to send custom headers.")]
    public bool SendHeaders { get; set; } = false;

    /// <summary>
    /// 静态请求头。
    /// </summary>
    [Description("Static headers to send with the request.")]
    [Hint(PresentationHint.KeyValueEditor)]
    [DisplayCondition(nameof(SendHeaders), true)]
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// 是否发送请求体。
    /// </summary>
    [Description("Whether to send a request body.")]
    public bool SendBody { get; set; } = false;

    /// <summary>
    /// Body 模板，支持 {placeholder}。
    /// </summary>
    [Description("Request body template. Use {placeholder} for dynamic values.")]
    [Hint(PresentationHint.TextArea)]
    [DisplayCondition(nameof(SendBody), true)]
    public string? Body { get; set; }

    /// <summary>
    /// 占位符定义列表。
    /// </summary>
    [Description("Define placeholders that LLM will fill. Name is the placeholder key, description helps LLM understand what to provide.")]
    public List<HttpPlaceholder>? Placeholders { get; set; }

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
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var inputData = context.GetInputDataAsDictionary();

            var resolvedUrl = NodeExecutionContext.ResolvePlaceholders(Url, inputData);
            if (string.IsNullOrWhiteSpace(resolvedUrl))
            {
                return context.ErrorResult("MissingUrl", "URL is required.");
            }

            var methodStr = Method.ToString().ToUpperInvariant();
            var request = new HttpRequestMessage(new HttpMethod(methodStr), resolvedUrl);

            if (Authentication != HttpRequestAuthMode.None && !string.IsNullOrEmpty(CredentialId))
            {
                var credentialValue = await context.ResolveApiKeyAsync(CredentialId, cancellationToken).ConfigureAwait(false);
                if (credentialValue is not null)
                {
                    switch (Authentication)
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

            if (SendHeaders && Headers is { Count: > 0 })
            {
                foreach (var (key, value) in Headers)
                {
                    var resolvedValue = NodeExecutionContext.ResolvePlaceholders(value, inputData);
                    request.Headers.TryAddWithoutValidation(key, resolvedValue);
                }
            }

            if (SendBody && !string.IsNullOrEmpty(Body) &&
                Method is HttpMethodOption.Post or HttpMethodOption.Put or HttpMethodOption.Patch)
            {
                var resolvedBody = NodeExecutionContext.ResolvePlaceholders(Body, inputData);
                request.Content = new StringContent(resolvedBody, Encoding.UTF8, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"));
            }

            return await HttpExecutionHelper.SendAndBuildResultAsync(request, context.Node.Id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return context.ErrorResult("Cancelled", "HTTP request was cancelled.");
        }
        catch (HttpRequestException ex)
        {
            return context.ErrorResult("HttpRequestFailed", $"HTTP request failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return context.ErrorResult("UnexpectedError", $"Unexpected HTTP error: {ex.Message}");
        }
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
