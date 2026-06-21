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
    Delete
}

/// <summary>
/// HTTP 请求节点，支持 GET/POST/PUT/DELETE 方法与凭据注入。
/// </summary>
public sealed class HttpRequestNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "httpRequest";

    /// <inheritdoc />
    public string DisplayName => "HTTP Request";

    /// <inheritdoc />
    public string Category => "HTTP";

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
    /// 目标 URL，支持表达式。
    /// </summary>
    [Description("Target URL. You can use expressions like {{ $input.url }}.")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 请求头键值对。
    /// </summary>
    [Description("Request headers as key-value pairs.")]
    [Hint(PresentationHint.KeyValueEditor)]
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// 请求体 JSON，仅 POST/PUT 时使用。
    /// </summary>
    [Description("Request body as JSON. Only used for POST/PUT.")]
    [DisplayCondition(nameof(Method), HttpMethodOption.Post)]
    [DisplayCondition(nameof(Method), HttpMethodOption.Put)]
    public JsonObject? Body { get; set; }

    /// <summary>
    /// API 凭据 ID。
    /// </summary>
    [Credential(FlowConstants.CredentialFields.ApiKey)]
    public string? ApiCredential { get; set; }

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
                return context.ErrorResult("MissingUrl", "URL 参数不能为空。");
            }

            var methodStr = Method.ToString().ToUpperInvariant();
            var request = new HttpRequestMessage(new HttpMethod(methodStr), Url);

            if (ApiCredential is { Length: > 0 } credentialIdStr && Guid.TryParse(credentialIdStr, out var credentialId))
            {
                try
                {
                    var credential = await context.Credentials.GetCredentialAsync(credentialId, context.CancellationToken)
                        .ConfigureAwait(false);
                    if (credential.Fields.TryGetValue(FlowConstants.CredentialFields.ApiKey, out var apiKey))
                    {
                        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
                    }
                }
                catch (Exception ex)
                {
                    return context.ErrorResult("CredentialError", $"凭据获取失败: {ex.Message}");
                }
            }

            if (Headers is { Count: > 0 })
            {
                foreach (var (key, value) in Headers)
                {
                    request.Headers.TryAddWithoutValidation(key, value);
                }
            }

            if (Method is HttpMethodOption.Post or HttpMethodOption.Put && Body is not null)
            {
                request.Content = new StringContent(Body.ToJsonString(), Encoding.UTF8, "application/json");
            }

            return await HttpExecutionHelper.SendAndBuildResultAsync(request, context.Node.Id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return context.ErrorResult("Cancelled", "HTTP 请求被取消。");
        }
        catch (HttpRequestException ex)
        {
            return context.ErrorResult("HttpRequestFailed", $"HTTP 请求异常: {ex.Message}");
        }
        catch (Exception ex)
        {
            return context.ErrorResult("UnexpectedError", $"HTTP 请求发生未预期错误: {ex.Message}");
        }
    }
}
