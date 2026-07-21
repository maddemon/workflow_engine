using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Core.Scripting;

/// <summary>
/// 对 Script 类型参数执行预求值的 Core 级实现，供运行时与仅依赖 Core 的调用方（如插件中的
/// <c>SubWorkflowExecutor</c>）复用，无需引用 Runtime。
/// Expression 脚本在 Hydrate 前完成求值并写入 <see cref="Script.ResolvedValue"/>；Script/CodeEditor 脚本
/// 保持原样；递归处理 <see cref="Dictionary{TKey,TValue}"/> 形式的列映射（string → Script）。
/// </summary>
public static class ScriptParameterPreEvaluatorCore
{
    /// <summary>
    /// 对 Script 类型参数执行预求值。会就地修改 <paramref name="rawParameters"/> 中命中的脚本值：
    /// 当参数定义的 <see cref="ParameterDefinition.Hint"/> 为 <see cref="PresentationHint.Expression"/> 时，
    /// 执行脚本并把结果写入 <see cref="Script.ResolvedValue"/>；否则保持原样。
    /// </summary>
    public static async Task PreEvaluateAsync(
        Dictionary<string, object> rawParameters,
        NodeTypeDescriptor descriptor,
        ScriptContext scriptContext,
        JsEngine js,
        ScriptCache scriptCache,
        CancellationToken cancellationToken)
    {
        foreach (var (name, value) in rawParameters.ToList())
        {
            var definition = descriptor.Parameters.FirstOrDefault(
                p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (value is Script script)
            {
                if (definition?.Hint == PresentationHint.Expression)
                {
                    var expressionResult = await EvaluateScriptAsync(script, scriptContext, js, scriptCache, cancellationToken)
                        .ConfigureAwait(false);
                    if (!expressionResult.Success)
                    {
                        throw new ScriptErrorException(script, $"参数表达式预求值失败: {expressionResult.Error?.Reason}", expressionResult.Error);
                    }

                    rawParameters[name] = script.WithResolvedValue(expressionResult.ToJson());
                }

                continue;
            }

            if (definition?.Type != ParameterType.Script
                && ScriptValueConverter.TryGetScriptDictionary(value, out var dict) && dict is not null)
            {
                if (definition?.Hint == PresentationHint.Expression)
                {
                    var evaluated = new Dictionary<string, Script>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (key, itemScript) in dict)
                    {
                        var itemResult = await EvaluateScriptAsync(itemScript, scriptContext, js, scriptCache, cancellationToken)
                            .ConfigureAwait(false);
                        if (!itemResult.Success)
                        {
                            throw new ScriptErrorException(itemScript, $"列映射表达式预求值失败: {itemResult.Error?.Reason}", itemResult.Error);
                        }

                        evaluated[key] = itemScript.WithResolvedValue(itemResult.ToJson());
                    }

                    rawParameters[name] = evaluated;
                }
                else
                {
                    rawParameters[name] = dict;
                }

                continue;
            }

            if (definition?.Type == ParameterType.Script)
            {
                var converted = ScriptValueConverter.ToScript(value);
                if (converted is null)
                {
                    continue;
                }

                if (definition.Hint == PresentationHint.Expression)
                {
                    var expressionResult = await EvaluateScriptAsync(converted, scriptContext, js, scriptCache, cancellationToken)
                        .ConfigureAwait(false);
                    if (!expressionResult.Success)
                    {
                        throw new ScriptErrorException(converted, $"参数表达式预求值失败: {expressionResult.Error?.Reason}", expressionResult.Error);
                    }

                    converted = converted.WithResolvedValue(expressionResult.ToJson());
                }

                rawParameters[name] = converted;
            }
        }
    }

    private static async Task<ScriptResult> EvaluateScriptAsync(
        Script script,
        ScriptContext context,
        JsEngine js,
        ScriptCache scriptCache,
        CancellationToken cancellationToken)
    {
        var prepared = scriptCache.GetOrPrepare(script);
        return await prepared.RunAsync(context, js, cancellationToken).ConfigureAwait(false);
    }
}
