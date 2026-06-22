using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Scripting;
using Jint;

namespace FlowEngine.Plugins.Standard;

public sealed class CodeSnippetToolNode : INodeType
{
    private const int DefaultTimeoutMs = 5000;

    public string TypeName => "codeSnippetTool";
    public string DisplayName => "Code Snippet Tool";
    public string Category => "AI";
    public string Icon => "code";
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input", DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "output", DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Tools, DisplayName = "Tool Output", Direction = PortDirection.Output, Type = PortType.AgentTool }
    ];

    public bool DefaultIsEntry => false;

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

            using var js = JsEngine.Create(options =>
            {
                options.TimeoutInterval(TimeSpan.FromMilliseconds(DefaultTimeoutMs));
                options.CancellationToken(cancellationToken);
            });

            if (codeInput is not null)
            {
                js.SetValue("input", codeInput);
            }

            var result = js.Run(code);
            var outputItem = JsEngine.ToDataItem(result);

            return Task.FromResult(new NodeExecutionResult
            {
                Success = true,
                Output = new DataBatch { Items = [outputItem] }
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
}
