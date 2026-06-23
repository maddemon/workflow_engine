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
/// HTTP 请求方法选项。
/// </summary>
public enum HttpMethodOption
{
    /// <summary>GET</summary>
    Get,

    /// <summary>POST</summary>
    Post,

    /// <summary>PUT</summary>
    Put,

    /// <summary>DELETE</summary>
    Delete,

    /// <summary>PATCH</summary>
    Patch
}

/// <summary>
/// HTTP 认证方式。
/// </summary>
public enum HttpRequestAuthMode
{
    /// <summary>无认证</summary>
    None,

    /// <summary>Bearer Token</summary>
    BearerToken,

    /// <summary>API Key</summary>
    ApiKey,

    /// <summary>Basic Auth</summary>
    BasicAuth
}

/// <summary>
/// HTTP 请求节点，支持静态配置和占位符。
/// 参考 n8n 的 HTTP Request 节点设计。
/// </summary>
public sealed class HttpRequestNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "httpRequest";

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
    /// 目标 URL，支持占位符 {placeholder}。
    /// </summary>
    [Description("Target URL. Use {placeholder} for dynamic values from input.")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 认证方式。
    /// </summary>
    [Description("Authentication method.")]
    public HttpRequestAuthMode Authentication { get; set; } = HttpRequestAuthMode.None;

    /// <summary>
    /// 凭据 ID（用于 Bearer Token 或 API Key）。
    /// </summary>
    [Credential(FlowConstants.CredentialFields.ApiKey)]
    [Description("Credential ID for authentication.")]
    public string? CredentialId { get; set; }

    /// <summary>
    /// 是否发送请求头。
    /// </summary>
    [Description("Whether to send custom headers.")]
    public bool SendHeaders { get; set; } = false;

    /// <summary>
    /// 请求头键值对。
    /// </summary>
    [Description("Request headers as key-value pairs.")]
    [Hint(PresentationHint.KeyValueEditor)]
    [DisplayCondition(nameof(SendHeaders), true)]
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// 是否发送请求体。
    /// </summary>
    [Description("Whether to send a request body.")]
    public bool SendBody { get; set; } = false;

    /// <summary>
    /// 请求体 JSON。
    /// </summary>
    [Description("Request body as JSON string.")]
    [Hint(PresentationHint.TextArea)]
    [DisplayCondition(nameof(SendBody), true)]
    public string? Body { get; set; }

    /// <summary>
    /// 占位符定义列表。
    /// </summary>
    [Description("Define placeholders for dynamic values (name → description).")]
    public List<HttpPlaceholder>? Placeholders { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Url))
            {
                return context.ErrorResult("MissingUrl", "URL is required.");
            }

            var inputData = context.GetInputDataAsDictionary();

            var resolvedUrl = NodeExecutionContext.ResolvePlaceholders(Url, inputData);
            if (string.IsNullOrWhiteSpace(resolvedUrl))
            {
                return context.ErrorResult("MissingUrl", "URL resolution failed.");
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

// HttpPlaceholder is defined in HttpToolNode.cs
