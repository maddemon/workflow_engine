using System.Text.Json;

namespace FlowEngine.Runtime.Registry.Converters;

/// <summary>
/// bool 类型转换策略。
/// </summary>
internal sealed class BoolConverter : IValueConverter
{
    public bool CanConvert(Type targetType) => targetType == typeof(bool);

    public Task<object?> ConvertAsync(object? value, Type targetType, ParameterHydratorContext context)
        => Task.FromResult<object?>(ConvertToBool(value!));

    private static bool? ConvertToBool(object value)
    {
        return value switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var result) ? result : s != "0",
            int i => i != 0,
            long l => l != 0,
            double d => d != 0,
            JsonElement element => element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(element.GetString(), out var r) && r,
                JsonValueKind.Number => element.GetInt32() != 0,
                _ => false
            },
            _ => false
        };
    }
}
