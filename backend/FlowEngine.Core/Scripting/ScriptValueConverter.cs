using System.Text.Json;
using System.Text.Json.Nodes;

namespace FlowEngine.Core.Scripting;

/// <summary>
/// Script 与 Dictionary&lt;string, Script&gt; 的统一转换逻辑。
/// 供 ParameterHydrator、NodeExecutionContextFactory、SubWorkflowExecutor 共享，消除三处重复。
/// </summary>
public static class ScriptValueConverter
{
    /// <summary>
    /// 将值转换为 Script。支持 Script、string、JsonElement、JsonNode。
    /// </summary>
    public static Script? ToScript(object? value)
    {
        return value switch
        {
            Script s => s,
            string str => new Script { Source = str, Language = ScriptLanguage.JavaScript, ReturnType = ScriptReturnType.String },
            JsonElement element => FromJsonElement(element),
            JsonNode node => FromJsonNode(node),
            _ => null
        };
    }

    private static Script? FromJsonElement(JsonElement element)
    {
        // 字符串令牌视为脚本源码，而非尝试反序列化为 Script 对象（否则会抛 JsonException）。
        if (element.ValueKind == JsonValueKind.String)
        {
            return new Script { Source = element.GetString()!, Language = ScriptLanguage.JavaScript, ReturnType = ScriptReturnType.String };
        }

        try
        {
            return element.Deserialize<Script>(JsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Script? FromJsonNode(JsonNode node)
    {
        // 字符串值视为脚本源码，避免对 JSON 字符串值调用 Deserialize<Script> 抛 JsonException。
        if (node is JsonValue value && value.TryGetValue<string>(out var str))
        {
            return new Script { Source = str, Language = ScriptLanguage.JavaScript, ReturnType = ScriptReturnType.String };
        }

        try
        {
            return node.Deserialize<Script>(JsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 尝试将值转换为 Dictionary&lt;string, Script&gt;。
    /// 支持 Dictionary、JsonElement(Object)、JsonObject、JsonNode、string(JSON)。
    /// </summary>
    public static bool TryGetScriptDictionary(object? value, out Dictionary<string, Script>? dict)
    {
        if (value is Dictionary<string, Script> d)
        {
            dict = d;
            return true;
        }

        try
        {
            if (value is JsonElement element)
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    dict = null;
                    return false;
                }

                dict = element.Deserialize<Dictionary<string, Script>>(JsonDefaults.Options);
                return dict is not null;
            }

            if (value is JsonObject obj)
            {
                dict = obj.Deserialize<Dictionary<string, Script>>(JsonDefaults.Options);
                return dict is not null;
            }

            if (value is JsonNode node)
            {
                if (node is not JsonObject)
                {
                    dict = null;
                    return false;
                }

                dict = node.Deserialize<Dictionary<string, Script>>(JsonDefaults.Options);
                return dict is not null;
            }

            if (value is string str)
            {
                dict = JsonSerializer.Deserialize<Dictionary<string, Script>>(str, JsonDefaults.Options);
                return dict is not null;
            }
        }
        catch (JsonException)
        {
            // 值不是 Dictionary<string, Script> 结构，返回 false
        }

        dict = null;
        return false;
    }
}
