using System.ComponentModel;
using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 代码执行工具节点，作为 Agent 的工具被调用。
/// 用户预定义代码，LLM 只提供输入参数。
/// </summary>
[NodeMeta(TypeName = "codeTool", DisplayName = "Code Tool", Category = NodeCategory.AI, Icon = "code", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
[Port(FlowConstants.PortNames.Tools, "Tool Output", PortDirection.Output, PortType.AgentTool)]
public sealed class CodeSnippetToolNode : NodeBase
{
    /// <summary>
    /// 预定义代码。
    /// </summary>
    [Description("JavaScript code to execute. Access LLM input via the 'input' variable.")]
    [Hint(PresentationHint.CodeEditor)]
    public Script Code { get; set; } = Script.Empty;

    /// <summary>
    /// 工具描述（帮助 LLM 理解何时调用此工具）。
    /// </summary>
    [Description("Tool description that helps LLM understand when to use this tool.")]
    public string ToolDescription { get; set; } = string.Empty;

    /// <inheritdoc />
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        if (Code is null || string.IsNullOrWhiteSpace(Code.Source))
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.MissingCode, "Code is required. Please define the code to execute.");
        }

        try
        {
            var inputPayload = input.InputBatch.Items.Count > 0 ? input.InputBatch.Items[0].Data : null;
            var inputData = GetInputData(inputPayload);

            var result = inputData is not null
                ? await Code.ExecuteAsync(ExecutionContext, ct, ("input", inputData)).ConfigureAwait(false)
                : await Code.ExecuteAsync(ExecutionContext, ct).ConfigureAwait(false);
            var outputItem = ToDataItem(result);

            return NodeHandlerOutput.Data(new DataBatch { Items = [outputItem] });
        }
        catch (ScriptErrorException ex)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.ScriptError, $"Script execution failed: {ex.Message}");
        }
        catch (Exception ex) when (ex is not NodeExecutionException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.UnexpectedError, ex.Message);
        }
    }

    private static object? GetInputData(JsonNode? payload)
    {
        if (payload is null)
        {
            return null;
        }

        // Convert JsonNode to a JS-friendly ExpandoObject so properties can be accessed via dot notation.
        var json = payload.ToJsonString();
        return JsonSerializer.Deserialize<ExpandoObject>(json, JsonDefaults.Options);
    }

    private static DataItem ToDataItem(ScriptResult result)
    {
        var json = result.ToJson();
        return new DataItem
        {
            Data = json,
            Success = true,
            SourceIndex = 0
        };
    }
}
