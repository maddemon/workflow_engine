using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Metadata;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 代码执行节点，使用 Jint 沙箱执行 JavaScript 代码。
/// 支持 Run Once for All Items 和 Run Once for Each Item 两种模式。
/// 新写法继承 <see cref="NodeBase"/>，经 <see cref="ScriptEvaluationExtensions.EvaluateAsync{T}"/> 复用节点托管引擎，
/// 业务失败统一抛 <see cref="NodeExecutionException"/>（不再使用 context.ErrorResult）。
/// </summary>
[NodeMeta(TypeName = "script", DisplayName = "Code", Category = NodeCategory.Data, Icon = "code")]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output)]
public sealed class JSNode : NodeBase
{
    [Inject] public NodeExecutionContext Ctx { get; private set; } = null!;

    /// <inheritdoc />
    protected override AiNodeDefinition? GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "Code (JavaScript)", "Core", false,
            "执行 JavaScript 代码转换数据。通过 $input.all() / $input.first() 访问上游输入，return 返回结果。支持 RunOnceForAllItems（一次性处理全部）与 RunOnceForEachItem（逐条处理）。",
            ["core", "code", "javascript", "transform"],
            JsonNode.Parse("""{"type":"object","description":"代码 return 的对象/数组"}"""),
            AiDefinitionHelpers.Example("拼接问候语",
                JsonNode.Parse("""{"codeMode":"RunOnceForAllItems","code":"return { message: $input.first().greeting + ' world' };"}"""),
                JsonNode.Parse("""{"message":"hello world"}""")));

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
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        if (Code is null || string.IsNullOrWhiteSpace(Code.Source))
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.MissingCode, "Code parameter is required.");
        }

        try
        {
            var result = CodeMode == CodeExecutionMode.RunOnceForEachItem
                ? await ExecuteForEachItem(input.InputBatch, ct).ConfigureAwait(false)
                : await ExecuteForAllItems(input.InputBatch, ct).ConfigureAwait(false);

            return NodeHandlerOutput.Data(result);
        }
        catch (OperationCanceledException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.Cancelled, "Code execution was cancelled.");
        }
        catch (ScriptErrorException ex)
        {
            throw new NodeExecutionException("CodeError", $"JavaScript execution error: {ex.Message}");
        }
        catch (TimeoutException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.Timeout, "Code execution timed out.");
        }
        catch (NodeExecutionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected error: {ex.Message}");
        }
    }

    private async Task<DataBatch> ExecuteForAllItems(DataBatch inputBatch, CancellationToken ct)
    {
        var allItems = inputBatch.Items.Select(i => (object?)i.Data).ToList();
        var currentItem = allItems.Count > 0 ? allItems[0] as JsonNode : null;

        // 标准逐项作用域（经 Script.EvaluateAsync 注入 $json/$input），currentItem 即 $json 与 $input.item()；
        // 框架已按与 NodeExecutionContextFactory 完全一致的方式构造 $input 容器，无需节点自行注入。
        var result = await Code.EvaluateAsync<JsonNode>(Ctx, item: currentItem, itemIndex: 0, cancellationToken: ct).ConfigureAwait(false);

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

            return new DataBatch { Items = items };
        }

        return new DataBatch { Items = [ToDataItem(result)] };
    }

    private async Task<DataBatch> ExecuteForEachItem(DataBatch inputBatch, CancellationToken ct)
    {
        var outputItems = new List<DataItem>();

        for (var itemIndex = 0; itemIndex < inputBatch.Items.Count; itemIndex++)
        {
            var item = inputBatch.Items[itemIndex];
            var result = await Code.EvaluateAsync<JsonNode>(Ctx, item: item.Data, itemIndex: itemIndex, cancellationToken: ct).ConfigureAwait(false);
            outputItems.Add(ToDataItem(result));
        }

        return new DataBatch { Items = outputItems };
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
