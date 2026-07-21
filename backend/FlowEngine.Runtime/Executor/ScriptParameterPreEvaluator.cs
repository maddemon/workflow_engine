using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 对 Script 类型参数执行预求值（运行时门面）。实际逻辑已下沉到
/// <see cref="ScriptParameterPreEvaluatorCore"/>，以便仅依赖 Core 的调用方（如 SubWorkflowExecutor 插件）复用。
/// Expression 脚本在 Hydrate 前完成求值并写入 <see cref="Script.ResolvedValue"/>。
/// </summary>
internal static class ScriptParameterPreEvaluator
{
    /// <summary>
    /// 对 Script 类型参数执行预求值：Expression 脚本直接求值并写入 ResolvedValue，
    /// Script/CodeEditor 脚本保持原样；递归处理 <see cref="Dictionary{TKey,TValue}"/> 形式的列映射（string → Script）。
    /// </summary>
    public static Task PreEvaluateAsync(
        Dictionary<string, object> rawParameters,
        NodeTypeDescriptor descriptor,
        ScriptContext scriptContext,
        JsEngine js,
        ScriptCache scriptCache,
        CancellationToken cancellationToken)
        => ScriptParameterPreEvaluatorCore.PreEvaluateAsync(rawParameters, descriptor, scriptContext, js, scriptCache, cancellationToken);
}
