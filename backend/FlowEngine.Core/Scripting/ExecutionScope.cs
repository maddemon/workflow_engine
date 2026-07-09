using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Scripting;

/// <summary>
/// 执行作用域注入的单一来源。
/// 集中把「全局变量（context.GlobalVariables）+ 逐项变量（$json/$input/$itemIndex/$runIndex）」注入 JsEngine，
/// 使所有节点使用与 NodeExecutionContextFactory（ParameterResolver 路径）完全相同的方式构造 $input，
/// 避免各节点自行注入导致的变量集漂移与 $input.params/$input.context 缺失等分歧。
///
/// 为支持逐 item 复用一个 JsEngine 实例（避免每行都 JsEngine.Create() 的开销），注入拆分为两层：
/// - <see cref="ApplyGlobalVariables"/>：全局变量，节点执行内恒定，只需调用一次；
/// - <see cref="ApplyItemScope"/>：逐项变量，每个 item 求值前调用一次（覆盖式）；
/// - <see cref="ApplyNodeScope"/>：一次性注入（组合上述两者），适用于无循环的简短求值。
/// </summary>
public static class ExecutionScope
{
    /// <summary>
    /// 注入全局变量（$credentials/$env/$workflow/$execution/$vars/$now/$today/$node/$ctx 等）。
    /// 这些值在单次节点执行内恒定，应在循环外调用一次。
    /// </summary>
    public static JsEngine ApplyGlobalVariables(this JsEngine engine, NodeExecutionContext context)
    {
        if (context.GlobalVariables is not null)
        {
            foreach (var (key, value) in context.GlobalVariables)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    engine.SetValue(key, value);
                }
            }
        }

        return engine;
    }

    /// <summary>
    /// 注入逐项变量（$json/$input/$itemIndex/$runIndex），每个 item 求值前调用一次（覆盖式）。
    /// 与工厂保持一致的方式构造 InputContainer，确保 $input.params/$input.context 可用。
    /// </summary>
    /// <param name="currentItem">当前 item 数据（对应 $json 与 $input.item()）。</param>
    /// <param name="allItems">当前节点全部输入 item（对应 $input.all()）。</param>
    /// <param name="itemIndex">当前 item 在批次中的索引。</param>
    public static JsEngine ApplyItemScope(
        this JsEngine engine,
        NodeExecutionContext context,
        JsonNode? currentItem,
        List<object?> allItems,
        int itemIndex)
    {
        var inputContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["executionId"] = context.ExecutionId,
            ["runIndex"] = itemIndex,
            ["nodeName"] = context.Node.Name,
            ["nodeType"] = context.Node.TypeName,
            ["workflowId"] = context.Workflow.Id,
        };
        var inputContainer = new InputContainer(allItems, currentItem, context.RawParameters, inputContext);

        engine.SetValue("$json", currentItem);
        engine.SetValue("$input", inputContainer);
        // 注：逐项节点沿用原实现语义，将 $runIndex 与 $itemIndex 同置为 itemIndex；
        // 工厂（OnceForAll）路径则使用节点 runIndex。两者语义不同由求值时机决定。
        engine.SetValue("$itemIndex", itemIndex);
        engine.SetValue("$runIndex", itemIndex);

        return engine;
    }

    /// <summary>
    /// 一次性注入全局 + 逐项变量（组合 ApplyGlobalVariables + ApplyItemScope）。
    /// 适用于无循环的简短求值；逐 item 循环场景请分别调用两层以避免重复创建引擎。
    /// </summary>
    public static JsEngine ApplyNodeScope(
        this JsEngine engine,
        NodeExecutionContext context,
        JsonNode? currentItem,
        List<object?> allItems,
        int itemIndex)
        => engine.ApplyGlobalVariables(context).ApplyItemScope(context, currentItem, allItems, itemIndex);
}
