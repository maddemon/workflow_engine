using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using Microsoft.Extensions.Options;

namespace FlowEngine.Core.Scripting;

    /// <summary>
    /// 提供 <see cref="Script"/> 在节点执行上下文中的便捷求值扩展（节点求值门面）。
    /// 节点只需声明 Script 属性并调用一次 <see cref="EvaluateAsync{T}"/> / <see cref="ExecuteAsync"/>，
    /// 缓存、预编译产物、引擎复用与逐项/额外全局作用域全部由本门面与运行时透明承担。
    /// 节点无需感知任何作用域类型：逐项求值传 <see cref="JsonNode"/>，额外全局传键值对，二者可叠加。
    /// </summary>
public static class ScriptEvaluationExtensions
{
    /// <summary>
    /// 主入口：求值并直接返回强类型值（覆盖绝大多数节点）。
    /// <list type="bullet">
    ///   <item>Expression 参数已被框架预求值（命中 <see cref="Script.ResolvedValue"/>）：纯 JsonNode 取值，零引擎、零执行。</item>
    ///   <item>其余：复用 <see cref="NodeExecutionContext"/> 托管的单个 JsEngine 执行；scope 提供逐项 / 额外全局变量。</item>
    /// </list>
    /// 取值逻辑集中在 <see cref="ScriptResult"/>，不产生第二套。
    /// </summary>
    /// <summary>
    /// 主入口（逐项求值）：传 <paramref name="item"/> 即按当前 item 逐项求值，框架注入标准 $json / $itemIndex；
    /// 需要额外全局变量时通过 <paramref name="globals"/> 传入（与上下文全局变量合并）。覆盖绝大多数节点。
    /// Expression 参数已被框架预求值（命中 <see cref="Script.ResolvedValue"/>）：纯 JsonNode 取值，零引擎、零执行。
    /// 其余：复用 <see cref="NodeExecutionContext"/> 托管的单个 JsEngine 执行。取值逻辑集中在 <see cref="ScriptResult"/>。
    /// </summary>
    /// <remarks>
    /// <paramref name="item"/> 必须保持为<b>必填</b>（不可加 <c>= null</c> 默认值）。否则
    /// <c>EvaluateAsync&lt;T&gt;(context, cancellationToken: ct)</c> 这类「无 item」调用会同时匹配本重载与
    /// 额外全局重载（第二参为 <see cref="CancellationToken"/>），诱发 CS0121 歧义。当前设计依赖 item 必填，使逐项重载
    /// 在缺 item 时直接「不适用」，从而唯一绑定到额外全局重载，无需依赖编译器对 params 的隐式偏好。
    /// </remarks>
    public static async Task<T?> EvaluateAsync<T>(
        this Script script,
        NodeExecutionContext context,
        JsonNode? item,
        int itemIndex = 0,
        (string Key, object? Value)[]? globals = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (script.ResolvedValue is not null)
        {
            return script.GetResult<T>();
        }

        var result = await script.ExecuteAsync(context, item, itemIndex, globals, cancellationToken).ConfigureAwait(false);
        return result.To<T>();
    }

    /// <summary>
    /// 主入口（额外全局）：第二个参数直接传键值对即可，框架自动作为额外全局变量注入（无需任何作用域类型）。
    /// 与 <see cref="EvaluateAsync{T}(Script, NodeExecutionContext, JsonNode?, int, (string, object?)[], CancellationToken)"/> 按第二个参数类型区分。
    /// </summary>
    public static Task<T?> EvaluateAsync<T>(
        this Script script,
        NodeExecutionContext context,
        CancellationToken cancellationToken,
        params (string Key, object? Value)[] globals)
        => script.EvaluateAsync<T>(context, item: null, itemIndex: 0, globals, cancellationToken);

    /// <summary>
    /// 次要入口（逐项求值）：需要原始 <see cref="ScriptResult"/>（判定 Success/Error，或同一次结果多种取值）时使用。
    /// 参数语义同逐项 <see cref="EvaluateAsync{T}"/>。
    /// </summary>
    public static async Task<ScriptResult> ExecuteAsync(
        this Script script,
        NodeExecutionContext context,
        JsonNode? item,
        int itemIndex = 0,
        (string Key, object? Value)[]? globals = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (script.ResolvedValue is not null)
        {
            return ScriptResult.FromResolved(script);
        }

        var prepared = GetOrCreateScriptCache(context).GetOrPrepare(script);
        var engine = context.GetOrCreateEngine();

        if (item is not null || itemIndex != 0)
        {
            var allItems = GetInputItems(context);
            engine.ApplyItemScope(context, item, allItems, itemIndex);
        }

        var scopeGlobals = globals is null || globals.Length == 0 ? null : ToDictionary(globals);
        var scriptContext = new ScriptContext(context, scopeGlobals);
        return await prepared.RunAsync(scriptContext, engine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 次要入口（额外全局）：第二个参数直接传键值对即可。与逐项 <see cref="ExecuteAsync"/> 按第二个参数类型区分。
    /// </summary>
    public static Task<ScriptResult> ExecuteAsync(
        this Script script,
        NodeExecutionContext context,
        CancellationToken cancellationToken,
        params (string Key, object? Value)[] globals)
        => script.ExecuteAsync(context, item: null, itemIndex: 0, globals, cancellationToken);

    private static IReadOnlyDictionary<string, object?> ToDictionary((string Key, object? Value)[] globals)
    {
        var dict = new Dictionary<string, object?>(globals.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in globals)
        {
            dict[key] = value;
        }

        return dict;
    }

    private static IScriptCache GetOrCreateScriptCache(NodeExecutionContext context)
    {
        return context.ScriptCache ?? new ScriptCache(Options.Create(new JsEngineOptions()));
    }

    private static List<object?> GetInputItems(NodeExecutionContext context)
    {
        if (!context.Inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch) || batch.Items.Count == 0)
        {
            return [];
        }

        return batch.Items.Select(i => (object?)i.Data).ToList();
    }
}


