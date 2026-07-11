using System.Text.Json;
using System.Text.Json.Nodes;

namespace FlowEngine.Runtime.Registry.Converters;

/// <summary>
/// string 类型转换策略。
/// </summary>
internal sealed class StringConverter : IValueConverter
{
    public bool CanConvert(Type targetType) => targetType == typeof(string);

    public Task<object?> ConvertAsync(object? value, Type targetType, ParameterHydratorContext context)
        => Task.FromResult<object?>(ConvertToString(value!));

    /// <summary>
    /// 将值转换为字符串。供 DateTimeConverter、UriConverter 复用。
    /// </summary>
    internal static string? ConvertToString(object value)
    {
        return value switch
        {
            string s => s,
            JsonNode node => node.ToJsonString(),
            JsonElement element => element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : element.GetRawText(),
            _ => value.ToString()
        };
    }
}
