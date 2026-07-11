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
            JsonElement element => element.Deserialize<Script>(JsonDefaults.Options),
            JsonNode node => node.Deserialize<Script>(JsonDefaults.Options),
            _ => null
        };
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
