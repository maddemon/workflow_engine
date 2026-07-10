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

        var scriptCache = context.ScriptCache ?? new ScriptCache(Options.Create(new JsEngineOptions()));
        var prepared = scriptCache.GetOrPrepare(script);
        var result = await prepared.RunAsync(ScriptContext.From(context), cancellationToken).ConfigureAwait(false);
        return result.To<T>();
    }
}
