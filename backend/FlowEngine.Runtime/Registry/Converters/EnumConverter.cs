using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Registry.Converters;

/// <summary>
/// 枚举类型转换策略。
/// </summary>
internal sealed class EnumConverter : IValueConverter
{
    public bool CanConvert(Type targetType) => targetType.IsEnum;

    public Task<object?> ConvertAsync(object? value, Type targetType, ParameterHydratorContext context)
        => Task.FromResult(ConvertToEnum(value!, targetType, context));

    private static object? ConvertToEnum(object value, Type enumType, ParameterHydratorContext context)
    {
        try
        {
            return value switch
            {
                string s => Enum.Parse(enumType, s, ignoreCase: true),
                int i => Enum.ToObject(enumType, i),
                long l => Enum.ToObject(enumType, l),
                JsonElement element when element.ValueKind == JsonValueKind.String
                    => Enum.Parse(enumType, element.GetString()!, ignoreCase: true),
                JsonElement element when element.ValueKind == JsonValueKind.Number
                    => Enum.ToObject(enumType, element.GetInt32()),
                _ => Enum.Parse(enumType, value.ToString()!, ignoreCase: true)
            };
        }
        catch (Exception ex)
        {
            var fallback = Enum.GetValues(enumType).GetValue(0);
            context.Logger?.LogWarning(
                ex, "枚举类型 {EnumType} 解析失败（值={Value}），使用默认值 {Default}。", enumType.Name, value, fallback);
            return fallback;
        }
    }
}
