using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Enums;
using Jint;
using Jint.Native;

namespace FlowEngine.Runtime.Scripting;

/// <summary>
/// 脚本执行引擎，统一处理 JS 表达式求值。
/// 支持不同的返回类型：string、object、bool、number。
/// </summary>
public static class ScriptEngine
{
    /// <summary>
    /// 执行脚本并返回指定类型的结果。
    /// </summary>
    /// <typeparam name="T">返回类型</typeparam>
    /// <param name="expression">JS 表达式</param>
    /// <param name="input">输入数据（绑定到 input 变量）</param>
    /// <param name="language">脚本语言</param>
    /// <returns>执行结果</returns>
    public static T? Evaluate<T>(string? expression, object? input, ScriptLanguage language = ScriptLanguage.JavaScript)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return default;
        }

        if (language != ScriptLanguage.JavaScript)
        {
            throw new NotSupportedException($"Script language '{language}' is not supported.");
        }

        try
        {
            using var js = JsEngine.Create();
            js.SetValue("input", input);
            var result = js.Evaluate(expression);
            return ConvertResult<T>(result);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// 执行脚本并返回字符串。
    /// </summary>
    public static string? EvaluateAsString(string? expression, object? input, ScriptLanguage language = ScriptLanguage.JavaScript)
    {
        return Evaluate<string>(expression, input, language);
    }

    /// <summary>
    /// 执行脚本并返回 JSON 对象（Dictionary&lt;string, string&gt;）。
    /// </summary>
    public static Dictionary<string, string>? EvaluateAsDictionary(string? expression, object? input, ScriptLanguage language = ScriptLanguage.JavaScript)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        try
        {
            using var js = JsEngine.Create();
            js.SetValue("input", input);
            var result = js.Evaluate(expression);

            var json = JsonSerializer.SerializeToNode(result.ToObject());
            if (json is JsonObject obj)
            {
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in obj)
                {
                    dict[prop.Key] = prop.Value?.ToString() ?? string.Empty;
                }
                return dict;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 执行脚本并返回 JsonObject。
    /// </summary>
    public static JsonObject? EvaluateAsJsonObject(string? expression, object? input, ScriptLanguage language = ScriptLanguage.JavaScript)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        try
        {
            using var js = JsEngine.Create();
            js.SetValue("input", input);
            var result = js.Evaluate(expression);

            var json = JsonSerializer.SerializeToNode(result.ToObject());
            return json as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 执行脚本并返回布尔值。
    /// </summary>
    public static bool EvaluateAsBool(string? expression, object? input, ScriptLanguage language = ScriptLanguage.JavaScript)
    {
        return Evaluate<bool>(expression, input, language);
    }

    private static T? ConvertResult<T>(JsValue result)
    {
        if (result.IsUndefined() || result.IsNull())
        {
            return default;
        }

        var targetType = typeof(T);

        if (targetType == typeof(string))
        {
            return (T)(object)result.ToString();
        }

        if (targetType == typeof(bool))
        {
            return (T)(object)result.AsBoolean();
        }

        if (targetType == typeof(int) || targetType == typeof(long))
        {
            return (T)(object)Convert.ToInt64(result.AsNumber());
        }

        if (targetType == typeof(double) || targetType == typeof(float))
        {
            return (T)(object)result.AsNumber();
        }

        // 对于复杂类型，返回 JSON 字符串
        return (T)(object)result.ToString();
    }
}
