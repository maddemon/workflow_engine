using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Http;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// HTTP 工具节点，发送 HTTP 请求，作为 tool 被 Agent 调用。
/// LLM 通过 input 端口传入 method、url、headers、body 参数。
/// </summary>
public sealed class HttpToolNode : INodeType
{
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
            var inputPayload = GetInputPayload(context);
            if (inputPayload is not JsonObject inputObj)
            {
                return context.ErrorResult("MissingInput", "Input JSON object is required with at least 'url' field.");
            }

            var url = inputObj["url"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(url))
            {
                return context.ErrorResult("MissingUrl", "The 'url' field is required in the input.");
            }

            var method = inputObj["method"]?.GetValue<string>()?.ToUpperInvariant() ?? "GET";
            var request = new HttpRequestMessage(new HttpMethod(method), url);

            var headers = ParseHeaders(inputObj["headers"]);
            if (headers is not null)
            {
                foreach (var (key, value) in headers)
                {
                    request.Headers.TryAddWithoutValidation(key, value);
                }
            }

            var body = inputObj["body"];
            if ((method == "POST" || method == "PUT" || method == "PATCH") && body is not null)
            {
                var bodyStr = body is JsonValue bodyVal && bodyVal.TryGetValue<string>(out var bodyText)
                    ? bodyText
                    : body.ToJsonString();
                request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
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

    private static JsonNode? GetInputPayload(NodeExecutionContext context)
    {
        if (!context.Inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch) || batch.Items.Count == 0)
        {
            return null;
        }

        return batch.Items[0].Data;
    }

    private static Dictionary<string, string>? ParseHeaders(JsonNode? headersNode)
    {
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
}
