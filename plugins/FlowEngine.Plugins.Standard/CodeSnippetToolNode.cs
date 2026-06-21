using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using Jint;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 代码片段工具节点，在 Jint 沙箱中执行用户代码片段，作为 tool 被 Agent 调用。
/// </summary>
public sealed class CodeSnippetToolNode : INodeType
{
    private const int DefaultTimeoutMs = 5000;

    /// <inheritdoc />
    public string TypeName => "codeSnippetTool";

    /// <inheritdoc />
    public string DisplayName => "Code Snippet Tool";

    /// <inheritdoc />
    public string Category => "Tool";

    /// <inheritdoc />
    public string Icon => "code";

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
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var inputPayload = GetInputPayload(context);
            if (inputPayload is null)
            {
                return Task.FromResult(context.ErrorResult("MissingInput", "Input data is required. Pass code and optional input via the input port."));
            }

            string? code = null;
            JsonNode? codeInput = null;

            if (inputPayload is JsonObject inputObj)
            {
                code = inputObj["code"]?.GetValue<string>();
                codeInput = inputObj["input"];
            }
            else if (inputPayload is JsonValue inputVal)
            {
                code = inputVal.GetValue<string>();
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return Task.FromResult(context.ErrorResult("MissingCode", "The 'code' field is required in the input."));
            }

            var engine = new Engine(options => options
                .TimeoutInterval(TimeSpan.FromMilliseconds(DefaultTimeoutMs))
                .LimitMemory(4_000_000)
                .CancellationToken(cancellationToken)
                .MaxStatements(1000));

            if (codeInput is not null)
            {
                var inputObjConverted = ConvertJsonNodeToObject(codeInput);
                engine.SetValue("input", inputObjConverted);
            }

            var wrappedCode = "(function() { " + code + " })()";
            var result = engine.Evaluate(wrappedCode);
            var outputData = ConvertToDataItem(result);

            return Task.FromResult(new NodeExecutionResult
            {
                Success = true,
                Output = new DataBatch
                {
                    Items = [outputData]
                }
            });
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(context.ErrorResult("Cancelled", "Code execution was cancelled."));
        }
        catch (Jint.Runtime.JavaScriptException ex)
        {
            return Task.FromResult(context.ErrorResult("CodeError", $"JavaScript execution error: {ex.Message}"));
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("Timeout"))
        {
            return Task.FromResult(context.ErrorResult("Timeout", "Code execution timed out."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(context.ErrorResult("UnexpectedError", $"Unexpected error during code execution: {ex.Message}"));
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

    private static DataItem ConvertToDataItem(Jint.Native.JsValue result)
    {
        if (result.IsUndefined() || result.IsNull())
        {
            return new DataItem { Data = null, Success = true, SourceIndex = 0 };
        }

        if (result.IsNumber())
        {
            return new DataItem { Data = JsonValue.Create(result.AsNumber()), Success = true, SourceIndex = 0 };
        }

        if (result.IsString())
        {
            return new DataItem { Data = JsonValue.Create(result.AsString()), Success = true, SourceIndex = 0 };
        }

        if (result.IsBoolean())
        {
            return new DataItem { Data = JsonValue.Create(result.AsBoolean()), Success = true, SourceIndex = 0 };
        }

        if (result.IsObject() || result.IsArray())
        {
            try
            {
                var dotNetValue = result.ToObject();
                var json = JsonSerializer.SerializeToNode(dotNetValue);
                return new DataItem { Data = json, Success = true, SourceIndex = 0 };
            }
            catch
            {
                // Fall through to string conversion
            }
        }

        var str = result.ToString();
        try
        {
            var json = JsonNode.Parse(str);
            return new DataItem { Data = json, Success = true, SourceIndex = 0 };
        }
        catch
        {
            return new DataItem { Data = JsonValue.Create(str), Success = true, SourceIndex = 0 };
        }
    }

    private static object ConvertJsonNodeToObject(JsonNode node)
    {
        var json = node.ToJsonString();
        var element = JsonSerializer.Deserialize<JsonElement>(json);

        return element.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonElementToDict(element),
            JsonValueKind.Array => ConvertJsonElementToList(element),
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => json
        };
    }

    private static Dictionary<string, object> ConvertJsonElementToDict(JsonElement element)
    {
        var dict = new Dictionary<string, object>();
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = ConvertJsonElementToObject(prop.Value);
        }
        return dict;
    }

    private static List<object> ConvertJsonElementToList(JsonElement element)
    {
        var list = new List<object>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ConvertJsonElementToObject(item));
        }
        return list;
    }

    private static object ConvertJsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonElementToDict(element),
            JsonValueKind.Array => ConvertJsonElementToList(element),
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.GetRawText()
        };
    }
}
