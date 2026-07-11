using FlowEngine.Core.Entities;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Runtime.Registry.Converters;

/// <summary>
/// Script 与 Dictionary&lt;string, Script&gt; 类型转换策略，委托给 ScriptValueConverter。
/// </summary>
internal sealed class ScriptConverter : IValueConverter
{
    public bool CanConvert(Type targetType)
    {
        if (targetType == typeof(Script))
        {
            return true;
        }

        return targetType.IsGenericType
            && targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
            && targetType.GetGenericArguments() is Type[] args
            && args.Length == 2
            && args[0] == typeof(string)
            && args[1] == typeof(Script);
    }

    public Task<object?> ConvertAsync(object? value, Type targetType, ParameterHydratorContext context)
    {
        if (targetType == typeof(Script))
        {
            return Task.FromResult<object?>(ScriptValueConverter.ToScript(value));
        }

        // Dictionary<string, Script>
        object? result = ScriptValueConverter.TryGetScriptDictionary(value, out var dict) ? dict : null;
        return Task.FromResult(result);
    }
}
