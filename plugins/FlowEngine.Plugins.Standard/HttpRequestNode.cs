using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Http;
using FlowEngine.Runtime.Scripting;

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
    /// 目标 URL，支持 JS 表达式（如 <c>"https://api.example.com/" + input.path</c>）。
    /// </summary>
    [DisplayName("URL")]
    [Description("Target URL. Use JS expression to build URL dynamically (e.g. 'https://api.com/' + input.path).")]
    [Hint(PresentationHint.Expression)]
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
    /// 是否发送自定义请求头。
    /// </summary>
    [DisplayName("Send Headers")]
    [Description("Whether to send custom headers.")]
    public bool SendHeaders { get; set; } = false;

    /// <summary>
    /// 请求头，JS 表达式，返回对象。
    /// </summary>
    [DisplayName("Headers")]
    [Description("Headers expression. Must return an object. Example: { 'Authorization': 'Bearer ' + input.token }")]
    [Hint(PresentationHint.Script, "language", ScriptLanguage.JavaScript, "returnType", "object")]
    [DisplayCondition(nameof(SendHeaders), true)]
    public string? HeadersExpression { get; set; }

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
    /// 请求体，JS 表达式，返回对象。
    /// </summary>
    [DisplayName("Body")]
    [Description("Body expression. Must return an object. Example: { name: input.name, count: input.count }")]
    [Hint(PresentationHint.Script, "language", ScriptLanguage.JavaScript, "returnType", "object")]
    [DisplayCondition(nameof(SendBody), true)]
    public string? BodyExpression { get; set; }

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

            var inputData = context.InputData;

            var resolvedUrl = ScriptEngine.EvaluateAsString(Url, inputData);
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

            if (SendHeaders && !string.IsNullOrEmpty(HeadersExpression))
            {
                var headers = ScriptEngine.EvaluateAsDictionary(HeadersExpression, inputData);
                if (headers is not null)
                {
                    foreach (var (key, value) in headers)
                    {
                        request.Headers.TryAddWithoutValidation(key, value);
                    }
                }
            }

            if (SendBody && !string.IsNullOrEmpty(BodyExpression) &&
                Method is HttpMethodOption.Post or HttpMethodOption.Put or HttpMethodOption.Patch)
            {
                var bodyJson = ScriptEngine.EvaluateAsString(BodyExpression, inputData) ?? string.Empty;
                request.Content = new StringContent(bodyJson, Encoding.UTF8, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"));
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
