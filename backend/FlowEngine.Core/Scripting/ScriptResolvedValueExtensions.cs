using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Scripting;
/// <summary>针对 <see cref="Script"/> 的已解析值读取扩展，供节点通过 <c>script.GetResolved&lt;T&gt;()</c> 获取强类型参数。</summary>
public static class ScriptResolvedValueExtensions
{
    /// <summary>获取脚本的已解析值为指定类型 T。</summary>
    /// <typeparam name="T">目标 CLR 类型。</typeparam>
    /// <param name="script">脚本实例。</param>
    /// <returns>解析后的强类型值。</returns>
    /// <exception cref="NodeParameterException">当 ResolvedValue 为 null 或无法转换为 T 时抛出。</exception>
    public static T GetResolved<T>(this Script script)
    {
        // ResolvedValue 已直接为 T（如 T 为 JsonNode）时直接返回，避免走转换路径。
        if (script.ResolvedValue is T direct)
        {
            return direct;
        }

        if (script.ResolvedValue is null)
        {
            throw new NodeParameterException("(script)", typeof(T));
        }

        // 复用 Script.GetResult<T>() 的类型转换（与 EvaluateAsync<T> 内置短路同源）。
        var result = script.GetResult<T>();
        if (result is null)
        {
            throw new NodeParameterException("(script)", typeof(T));
        }

        return result;
    }
}
