using FlowEngine.Core;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 代码执行节点，使用 Jint 沙箱执行 JavaScript 代码。
/// 支持 Run Once for All Items 和 Run Once for Each Item 两种模式。
/// </summary>
public sealed class JSNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "script";

    /// <inheritdoc />
    AiNodeDefinition? INodeType.GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "Code (JavaScript)", "Core", false,
            "执行 JavaScript 代码转换数据。通过 $input.all() / $input.first() 访问上游输入，return 返回结果。支持 RunOnceForAllItems（一次性处理全部）与 RunOnceForEachItem（逐条处理）。",
            ["core", "code", "javascript", "transform"],
            JsonNode.Parse("""{"type":"object","description":"代码 return 的对象/数组"}"""),
            AiDefinitionHelpers.Example("拼接问候语",
                JsonNode.Parse("""{"codeMode":"RunOnceForAllItems","code":"return { message: $input.first().greeting + ' world' };"}"""),
                JsonNode.Parse("""{"message":"hello world"}""")));

    /// <inheritdoc />
    public string DisplayName => "Code";

    /// <inheritdoc />
    public string Category => "Data";

    /// <inheritdoc />
    public string Icon => "code";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 执行模式。
    /// </summary>
    [Description("Run code once for all items or once for each item.")]
    public CodeExecutionMode CodeMode { get; set; } = CodeExecutionMode.RunOnceForAllItems;

    /// <summary>
    /// 要执行的代码。
    /// </summary>
    [Description("JavaScript code to execute. Access input via $input.all() or $input.first().")]
    [Hint(PresentationHint.CodeEditor)]
    public Script Code { get; set; } = Script.Empty;

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
            if (Code is null || string.IsNullOrWhiteSpace(Code.Source))
            {
                return context.ErrorResult(FlowConstants.ErrorCodes.MissingCode, "Code parameter is required.");
            }

            var inputBatch = context.GetInputBatch();

            if (CodeMode == CodeExecutionMode.RunOnceForEachItem)
            {
                return await ExecuteForEachItem(inputBatch, context, cancellationToken).ConfigureAwait(false);
            }

            return await ExecuteForAllItems(inputBatch, context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.Cancelled, "Code execution was cancelled.");
        }
        catch (ScriptErrorException ex)
        {
            return context.ErrorResult("CodeError", $"JavaScript execution error: {ex.Message}");
        }
        catch (TimeoutException)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.Timeout, "Code execution timed out.");
        }
        catch (Exception ex)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected error: {ex.Message}");
        }
    }

    private async Task<NodeExecutionResult> ExecuteForAllItems(
        DataBatch inputBatch,
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var allItems = inputBatch.Items.Select(i => (object?)i.Data).ToList();
        var currentItem = allItems.Count > 0 ? allItems[0] as JsonNode : null;

        var inputContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["executionId"] = context.ExecutionId,
            ["runIndex"] = context.RunIndex,
            ["nodeName"] = context.Node.Name,
            ["nodeType"] = context.Node.TypeName,
            ["workflowId"] = context.Workflow.Id,
        };

        // item 自动注入 $json；仅需额外暴露 $input 容器。
        var result = await Code.EvaluateAsync<JsonNode>(context, currentItem,
            globals: new (string, object?)[]
            {
                ("$input", new InputContainer(allItems, currentItem, context.RawParameters, inputContext)),
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        // 返回值为 JsonArray 时，每个元素展开为一个独立 DataItem（对齐 n8n normalizeItems），
        // 使下游节点（如 dbUpsert）能逐 item 处理，而非把整个数组当成单个 item 的 Data。
        if (result is JsonArray array)
        {
            var items = new List<DataItem>(array.Count);
            for (var i = 0; i < array.Count; i++)
            {
                items.Add(new DataItem
                {
                    Data = array[i]?.DeepClone(),
                    Success = true,
                    SourceIndex = i
                });
            }

            return new NodeExecutionResult
            {
                Success = true,
                Output = new DataBatch { Items = items }
            };
        }

        var outputItem = ToDataItem(result);

        return new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = [outputItem] }
        };
    }

    private async Task<NodeExecutionResult> ExecuteForEachItem(
        DataBatch inputBatch,
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var outputItems = new List<DataItem>();

        for (var itemIndex = 0; itemIndex < inputBatch.Items.Count; itemIndex++)
        {
            var item = inputBatch.Items[itemIndex];
            var result = await Code.EvaluateAsync<JsonNode>(context, item.Data, itemIndex, cancellationToken: cancellationToken).ConfigureAwait(false);
            outputItems.Add(ToDataItem(result));
        }

        return new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = outputItems }
        };
    }

    private static DataItem ToDataItem(JsonNode? json)
    {
        return new DataItem
        {
            Data = json,
            Success = true,
            SourceIndex = 0
        };
    }
}

/// <summary>
/// 代码执行模式。
/// </summary>
public enum CodeExecutionMode
{
    /// <summary>所有项目执行一次</summary>
    RunOnceForAllItems,

    /// <summary>每个项目执行一次</summary>
    RunOnceForEachItem
}
