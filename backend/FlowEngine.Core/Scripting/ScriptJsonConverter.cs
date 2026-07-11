using System.Buffers;
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

        if (reader.TokenType == JsonTokenType.True)
        {
            return new Script("true", ScriptLanguage.JavaScript, ScriptReturnType.Object);
        }

        if (reader.TokenType == JsonTokenType.False)
        {
            return new Script("false", ScriptLanguage.JavaScript, ScriptReturnType.Object);
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            string numberText;
            if (reader.TryGetDecimal(out var decimalValue))
            {
                numberText = decimalValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                // 超出 decimal 范围（如科学计数法大数）：保留原始 JSON 数字文本，避免 OverflowException。
                var bytes = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray();
                numberText = System.Text.Encoding.UTF8.GetString(bytes);
            }

            return new Script(numberText, ScriptLanguage.JavaScript, ScriptReturnType.Object);
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
