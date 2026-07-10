using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FlowEngine.Core.Scripting;

/// <summary>
/// <see cref="Script"/> 的 JSON 转换器。
/// 支持对象形式 <c>{ "source": "...", "language?": "...", "returnType?": "..." }</c>
/// 与纯字符串简写。
/// </summary>
public sealed class ScriptJsonConverter : JsonConverter<Script>
{
    /// <inheritdoc />
    public override Script? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return new Script(reader.GetString() ?? string.Empty, ScriptLanguage.JavaScript, ScriptReturnType.Object);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Script JSON 必须是对象或字符串。");
        }

        var node = JsonNode.Parse(ref reader)
                   ?? throw new JsonException("无法解析 Script JSON 对象。");

        var source = node["source"]?.GetValue<string>() ?? string.Empty;
        var language = EnumValueOrDefault(node["language"], ScriptLanguage.JavaScript);
        var returnType = EnumValueOrDefault(node["returnType"], ScriptReturnType.Object);

        return new Script(source, language, returnType);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Script value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("source", value.Source);

        if (value.Language != ScriptLanguage.JavaScript)
        {
            writer.WriteString("language", value.Language.ToString());
        }

        if (value.ReturnType != ScriptReturnType.Object)
        {
            writer.WriteString("returnType", value.ReturnType.ToString());
        }

        writer.WriteEndObject();
    }

    private static TEnum EnumValueOrDefault<TEnum>(JsonNode? node, TEnum defaultValue) where TEnum : struct, Enum
    {
        if (node is null)
        {
            return defaultValue;
        }

        var text = node.GetValue<string>();
        return Enum.TryParse<TEnum>(text, true, out var value)
            ? value
            : defaultValue;
    }
}
