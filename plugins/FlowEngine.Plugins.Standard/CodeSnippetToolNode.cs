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
    public Script Code { get; set; } = Script.Empty;

    /// <summary>
    /// 工具描述（帮助 LLM 理解何时调用此工具）。
    /// </summary>
    [Description("Tool description that helps LLM understand when to use this tool.")]
    public string ToolDescription { get; set; } = string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Tools, DisplayName = "Tool Output", Direction = PortDirection.Output, Type = PortType.AgentTool }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (Code is null || string.IsNullOrWhiteSpace(Code.Source))
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.MissingCode, "Code is required. Please define the code to execute.");
        }

        return await context.CatchToResult(async ct =>
        {
            // Get input from LLM
            var inputPayload = context.GetInputPayload();
            var inputData = GetInputData(inputPayload);

            var result = inputData is not null
                ? await Code.ExecuteAsync(context, ct, ("input", inputData)).ConfigureAwait(false)
                : await Code.ExecuteAsync(context, ct).ConfigureAwait(false);
            var outputItem = ToDataItem(result);

            return new NodeExecutionResult
            {
                Success = true,
                Output = new DataBatch { Items = [outputItem] }
            };
        }, cancellationToken).ConfigureAwait(false);
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
