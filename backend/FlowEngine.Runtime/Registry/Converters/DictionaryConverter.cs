using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Registry.Converters;

/// <summary>
/// 泛型 Dictionary&lt;,&gt; 类型转换策略。
/// </summary>
/// <remarks>
/// 注意：Dictionary&lt;string, Script&gt; 由 <see cref="ScriptConverter"/> 优先处理，
/// 本 converter 仅处理其他 Dictionary&lt;,&gt; 类型。
/// </remarks>
internal sealed class DictionaryConverter : IValueConverter
{
    public bool CanConvert(Type targetType)
        => targetType.IsGenericType
            && targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>);

    public Task<object?> ConvertAsync(object? value, Type targetType, ParameterHydratorContext context)
        => Task.FromResult(ConvertToDictionary(value!, targetType, context));

    private static object? ConvertToDictionary(object value, Type dictType, ParameterHydratorContext context)
    {
        try
        {
            return value switch
            {
                JsonElement element => JsonSerializer.Deserialize(element.GetRawText(), dictType),
                string s => JsonSerializer.Deserialize(s, dictType),
                JsonNode node => JsonSerializer.Deserialize(node.ToJsonString(), dictType),
                _ => null
            };
        }
        catch (Exception ex)
        {
            context.Logger?.LogWarning(ex, "字典类型 {DictType} 反序列化失败。", dictType.Name);
            return null;
        }
    }
}
