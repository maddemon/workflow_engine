using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Exceptions;
using Jint;
using Jint.Native;

namespace FlowEngine.Core.Scripting;

/// <summary>
/// 脚本执行结果，封装原始 JsValue 与转换辅助方法。
/// </summary>
public sealed class ScriptResult
{
    /// <summary>
    /// 原始脚本。
    /// </summary>
    public Script Original { get; }

    /// <summary>
    /// 是否执行成功。
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Jint 原始返回值。失败时为 <see cref="JsValue.Undefined"/>。
    /// </summary>
    public JsValue Raw { get; }

    /// <summary>
    /// 执行失败时的异常信息。
    /// </summary>
    public ScriptErrorException? Error { get; }

    /// <summary>
    /// 初始化成功的脚本结果。
    /// </summary>
    public ScriptResult(Script original, JsValue raw)
    {
        Original = original ?? throw new ArgumentNullException(nameof(original));
        Raw = raw;
        Success = true;
    }

    /// <summary>
    /// 初始化失败的脚本结果。
    /// </summary>
    public ScriptResult(Script original, ScriptErrorException error)
    {
        Original = original ?? throw new ArgumentNullException(nameof(original));
        Error = error ?? throw new ArgumentNullException(nameof(error));
        Raw = JsValue.Undefined;
        Success = false;
    }

    /// <summary>
    /// 将结果转换为 CLR 对象（原 <see cref="JsEngine.ToClrValue"/> 语义）。
    /// </summary>
    public object? ToClr()
    {
        EnsureSuccess();

        if (Raw.IsUndefined() || Raw.IsNull())
        {
            return null;
        }

        if (Raw.IsBoolean())
        {
            return Raw.AsBoolean();
        }

        if (Raw.IsNumber())
        {
            var num = Raw.AsNumber();
            return num == Math.Floor(num) && num is >= int.MinValue and <= int.MaxValue
                ? (int)num
                : num;
        }

        if (Raw.IsString())
        {
            return Raw.AsString();
        }

        return ToJson();
    }

    /// <summary>
    /// 将结果按 JavaScript 真值语义转换为布尔值。
    /// </summary>
    public bool ToBoolean()
    {
        EnsureSuccess();

        if (Raw.IsUndefined() || Raw.IsNull())
        {
            return false;
        }

        if (Raw.IsBoolean())
        {
            return Raw.AsBoolean();
        }

        if (Raw.IsNumber())
        {
            var num = Raw.AsNumber();
            return !double.IsNaN(num) && num != 0;
        }

        if (Raw.IsString())
        {
            return Raw.AsString().Length > 0;
        }

        // 数组与对象在 JS 中均为 truthy。
        if (Raw.IsArray() || Raw.IsObject())
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 将结果转换为 <see cref="JsonNode"/>。
    /// </summary>
    public JsonNode? ToJson()
    {
        EnsureSuccess();

        if (Raw.IsUndefined() || Raw.IsNull())
        {
            return null;
        }

        if (Raw.IsBoolean())
        {
            return JsonValue.Create(Raw.AsBoolean());
        }

        if (Raw.IsNumber())
        {
            var num = Raw.AsNumber();
            if (num == Math.Floor(num) && num is >= int.MinValue and <= int.MaxValue)
            {
                return JsonValue.Create((int)num);
            }

            return JsonValue.Create(num);
        }

        if (Raw.IsString())
        {
            return JsonValue.Create(Raw.AsString());
        }

        try
        {
            var obj = Raw.ToObject();
            return JsonSerializer.SerializeToNode(obj, JsonDefaults.Options);
        }
        catch
        {
            var str = Raw.ToString();
            try
            {
                return JsonNode.Parse(str);
            }
            catch
            {
                return JsonValue.Create(str);
            }
        }
    }

    /// <summary>
    /// 将结果转换为指定 CLR 类型。使用 <see cref="Script.ReturnType"/> 作为提示，
    /// 但仍会尽最大努力转换。
    /// </summary>
    public T? To<T>()
    {
        EnsureSuccess();

        var targetType = typeof(T);

        if (targetType == typeof(string))
        {
            return (T?)(object?)Raw.ToString();
        }

        if (targetType == typeof(bool))
        {
            return (T?)(object)ToBoolean();
        }

        if (targetType == typeof(int) || targetType == typeof(long) || targetType == typeof(double))
        {
            if (!Raw.IsNumber())
            {
                return default;
            }

            var num = Raw.AsNumber();
            if (targetType == typeof(int))
            {
                return (T?)(object)Convert.ToInt32(num);
            }

            if (targetType == typeof(long))
            {
                return (T?)(object)Convert.ToInt64(num);
            }

            return (T?)(object)num;
        }

        if (targetType == typeof(JsonNode) || targetType == typeof(JsonObject) || targetType == typeof(JsonArray))
        {
            var json = ToJson();
            if (json is T t) return t;
            return default;
        }

        if (Original.ReturnType == ScriptReturnType.Dictionary && targetType == typeof(Dictionary<string, string>))
        {
            var json = ToJson();
            if (json is JsonObject obj)
            {
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in obj)
                {
                    dict[prop.Key] = prop.Value?.ToString() ?? string.Empty;
                }

                return (T?)(object)dict;
            }

            return default;
        }

        var clr = ToClr();
        if (clr is T direct) return direct;
        if (clr is null) return default;

        try
        {
            var json = JsonSerializer.Serialize(clr, JsonDefaults.Options);
            return JsonSerializer.Deserialize<T>(json, JsonDefaults.Options);
        }
        catch
        {
            return default;
        }
    }

    private void EnsureSuccess()
    {
        if (!Success)
        {
            throw Error ?? new ScriptErrorException(Original, "脚本执行失败");
        }
    }
}
