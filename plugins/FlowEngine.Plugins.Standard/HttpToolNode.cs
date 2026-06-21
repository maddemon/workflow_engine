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
/// HTTP 工具节点，发送 HTTP 请求，作为 tool 被 Agent 调用。
/// LLM 通过 input 端口传入 method、url、headers、body 参数。
/// </summary>
public sealed class HttpToolNode : INodeType
{
    private static readonly HttpClient SharedHttpClient = new();

    /// <inheritdoc />
    public string TypeName => "httpTool";

    /// <inheritdoc />
    public string DisplayName => "HTTP Tool";

    /// <inheritdoc />
    public string Category => "Tool";

    /// <inheritdoc />
    public string Icon => "globe";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

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
            var inputPayload = GetInputPayload(context);
            if (inputPayload is null || inputPayload is not JsonObject inputObj)
            {
                return context.ErrorResult("MissingInput", "Input JSON object is required with at least 'url' field.");
            }

            var url = inputObj["url"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(url))
            {
                return context.ErrorResult("MissingUrl", "The 'url' field is required in the input.");
            }

            var method = inputObj["method"]?.GetValue<string>()?.ToUpperInvariant() ?? "GET";
            var headers = ParseHeaders(inputObj["headers"]);
            var body = inputObj["body"];

            var request = new HttpRequestMessage(new HttpMethod(method), url);

            if (headers is not null)
            {
                foreach (var (key, value) in headers)
                {
                    request.Headers.TryAddWithoutValidation(key, value);
                }
            }

            if ((method == "POST" || method == "PUT" || method == "PATCH") && body is not null)
            {
                var bodyStr = body is JsonValue bodyVal && bodyVal.TryGetValue<string>(out var bodyText)
                    ? bodyText
                    : body.ToJsonString();
                request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
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
                        Message = $"HTTP request failed: {response.StatusCode}",
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

    private static JsonNode? GetInputPayload(NodeExecutionContext context)
    {
        if (!context.Inputs.TryGetValue("input", out var batch) || batch.Items.Count == 0)
        {
            return null;
        }

        return batch.Items[0].Data;
    }

    private static Dictionary<string, string>? ParseHeaders(JsonNode? headersNode)
    {
        if (headersNode is null)
        {
            return null;
        }

        if (headersNode is not JsonObject headersObj)
        {
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in headersObj)
        {
            if (prop.Value is JsonValue val && val.TryGetValue<string>(out var strVal))
            {
                headers[prop.Key] = strVal;
            }
        }

        return headers.Count > 0 ? headers : null;
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
