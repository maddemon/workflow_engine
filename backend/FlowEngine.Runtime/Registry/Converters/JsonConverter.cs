using System.Text.Json;
using System.Text.Json.Nodes;

namespace FlowEngine.Runtime.Registry.Converters;

/// <summary>
/// JsonObject/JsonNode 类型转换策略。
/// </summary>
internal sealed class JsonConverter : IValueConverter
{
    public bool CanConvert(Type targetType)
        => typeof(JsonObject).IsAssignableFrom(targetType)
            || typeof(JsonNode).IsAssignableFrom(targetType);

    public Task<object?> ConvertAsync(object? value, Type targetType, ParameterHydratorContext context)
    {
        // JsonObject 检查优先于 JsonNode
        object? result = typeof(JsonObject).IsAssignableFrom(targetType)
            ? ConvertToJsonObject(value!)
            : ConvertToJsonNode(value!);
        return Task.FromResult(result);
    }

    private static JsonObject? ConvertToJsonObject(object value)
    {
        return value switch
        {
            JsonObject obj => obj,
            JsonNode node => node is JsonObject jo ? jo : null,
            string s when !string.IsNullOrWhiteSpace(s) => JsonNode.Parse(s)?.AsObject(),
            JsonElement element => element.ValueKind == JsonValueKind.Object
                ? JsonObject.Create(element)
                : null,
            _ => null
        };
    }

    private static JsonNode? ConvertToJsonNode(object value)
    {
        return value switch
        {
            JsonNode node => node,
            string s => JsonNode.Parse(s),
            JsonElement element => JsonNode.Parse(element.GetRawText()),
            _ => null
        };
    }
}
