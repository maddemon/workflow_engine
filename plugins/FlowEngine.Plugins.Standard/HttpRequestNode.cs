using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

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
    private static readonly HttpClient SharedHttpClient = new();

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
    [Credential("apiKey")]
    public string? ApiCredential { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input", DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "output", DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var methodStr = Method.ToString().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(Url))
            {
                return context.ErrorResult("MissingUrl", "URL 参数不能为空。");
            }

            var request = new HttpRequestMessage(new HttpMethod(methodStr), Url);

            if (ApiCredential is { Length: > 0 } credentialIdStr && Guid.TryParse(credentialIdStr, out var credentialId))
            {
                try
                {
                    var credential = await context.Credentials.GetCredentialAsync(credentialId, context.CancellationToken)
                        .ConfigureAwait(false);
                    if (credential.Fields.TryGetValue("apiKey", out var apiKey))
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

            using var response = await SharedHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var output = new JsonObject
            {
                ["statusCode"] = (int)response.StatusCode,
                ["statusText"] = response.StatusCode.ToString(),
                ["body"] = TryParseJson(responseBody, out var jsonNode) ? jsonNode : responseBody,
                ["headers"] = SerializeResponseHeaders(response)
            };

            return new NodeExecutionResult
            {
                Success = response.StatusCode < HttpStatusCode.BadRequest,
                Output = new DataBatch
                {
                    Items =
                    [
                        new DataItem
                        {
                            Data = output,
                            Success = response.StatusCode < HttpStatusCode.BadRequest,
                            SourceIndex = 0
                        }
                    ]
                },
                Error = response.StatusCode >= HttpStatusCode.BadRequest
                    ? new NodeError
                    {
                        Code = "HttpError",
                        Message = $"HTTP 请求失败: {response.StatusCode}",
                        NodeDefinitionId = context.Node.Id,
                        Details = new Dictionary<string, string>
                        {
                            ["statusCode"] = ((int)response.StatusCode).ToString()
                        }
                    }
                    : null
            };
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

    private static bool TryParseJson(string json, out JsonNode? node)
    {
        try
        {
            node = JsonNode.Parse(json);
            return true;
        }
        catch (JsonException)
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
