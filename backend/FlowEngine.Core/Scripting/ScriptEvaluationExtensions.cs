using FlowEngine.Core.Entities;
using Microsoft.Extensions.Options;

namespace FlowEngine.Core.Scripting;

/// <summary>
/// 提供 <see cref="Script"/> 在节点执行上下文中的便捷求值扩展。
/// </summary>
public static class ScriptEvaluationExtensions
{
    /// <summary>
    /// 对 Expression 类型的 <see cref="Script"/> 参数求值，优先使用已解析值。
    /// </summary>
    public static async Task<T?> EvaluateExpressionAsync<T>(
        this Script script,
        NodeExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (script.ResolvedValue is not null)
        {
            return script.GetResult<T>();
        }

        var scriptCache = context.GetScriptCache();
        var prepared = scriptCache.GetOrPrepare(script);
        var result = await prepared.RunAsync(ScriptContext.From(context), cancellationToken).ConfigureAwait(false);
        return result.To<T>();
    }
}

/// <summary>
/// <see cref="NodeExecutionContext"/> 获取 <see cref="IScriptCache"/> 的扩展。
/// </summary>
public static class ScriptCacheContextExtensions
{
    /// <summary>
    /// 获取上下文中的脚本缓存。工厂保证 <see cref="NodeExecutionContext.ScriptCache"/> 非空；
    /// 此分支仅用于脱离工厂的单元测试上下文，回退到默认选项（含标准安全黑名单）。
    /// </summary>
    public static IScriptCache GetScriptCache(this NodeExecutionContext context)
    {
        if (context.ScriptCache is not null)
        {
            return context.ScriptCache;
        }

        return new ScriptCache(Options.Create(new JsEngineOptions()));
    }
}
