using System.ComponentModel;
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

/// <summary>
/// 代码执行工具节点，作为 Agent 的工具被调用。
/// 用户预定义代码，LLM 只提供输入参数。
/// </summary>
public sealed class CodeSnippetToolNode : INodeType
{
    private const int DefaultTimeoutMs = 5000;

    /// <inheritdoc />
    public string TypeName => "codeTool";

    /// <inheritdoc />
    public string DisplayName => "Code Tool";

    /// <inheritdoc />
    public string Category => "AI";

    /// <inheritdoc />
    public string Icon => "code";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 预定义代码。
    /// </summary>
    [Description("JavaScript code to execute. Access LLM input via the 'input' variable.")]
    [Hint(PresentationHint.CodeEditor)]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 工具描述（帮助 LLM 理解何时调用此工具）。
    /// </summary>
    [Description("Tool description that helps LLM understand when to use this tool.")]
    public string ToolDescription { get; set; } = string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input", DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "output", DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Tools, DisplayName = "Tool Output", Direction = PortDirection.Output, Type = PortType.AgentTool }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Code))
            {
                return Task.FromResult(context.ErrorResult("MissingCode", "Code is required. Please define the code to execute."));
            }

            // Get input from LLM
            var inputPayload = GetInputPayload(context);
            var inputData = GetInputData(inputPayload);

            using var js = JsEngine.Create();

            // Provide input to JS code
            if (inputData is not null)
            {
                js.SetValue("input", inputData);
            }

            var result = js.Run(Code);
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

    private static object? GetInputData(JsonNode? payload)
    {
        if (payload is null)
        {
            return null;
        }

        // Convert JsonNode to object for Jint
        var json = payload.ToJsonString();
        return JsonSerializer.Deserialize<object>(json);
    }
}
