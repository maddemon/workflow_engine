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
    /// 已预求值路径的 JsonNode（命中 Script.ResolvedValue 时）。
    /// </summary>
    private readonly JsonNode? _resolvedNode;

    /// <summary>
    /// 由 <see cref="_resolvedNode"/> 惰性转换并缓存的 JsValue。
    /// </summary>
    private JsValue? _resolvedRaw;

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
    /// 从已预求值的 <see cref="Script.ResolvedValue"/> 构造结果（快路径，不执行脚本、不建引擎）。
    /// 内部将 JsonNode 转为 JsValue，以便复用 To&lt;T&gt;/ToClr/ToBoolean/ToJson 统一取值语义。
    /// </summary>
    internal ScriptResult(Script original, JsonNode? resolvedValue)
    {
        Original = original ?? throw new ArgumentNullException(nameof(original));
        _resolvedNode = resolvedValue;
        Raw = JsValue.Undefined;
        Success = true;
    }

    /// <summary>
    /// 从已预求值的脚本构造结果（内部快路径入口）。
    /// </summary>
    internal static ScriptResult FromResolved(Script script) => new(script, script.ResolvedValue);

    /// <summary>
    /// 解析实际参与取值的 JsValue：已预求值路径惰性将 JsonNode 转为 JsValue（线程内复用单个引擎），
    /// 否则返回执行得到的 <see cref="Raw"/>。
    /// </summary>
    private JsValue ResolveRaw()
    {
        if (_resolvedNode is null)
        {
            return Raw;
        }

        if (_resolvedRaw is not null)
        {
            return _resolvedRaw;
        }

        // 仅用引擎解析 JSON 字面量（不执行任何用户脚本），线程内只创建一次。
        _convertEngine ??= new Engine();
        _resolvedRaw = _convertEngine.Evaluate(_resolvedNode.ToJsonString() ?? "null");
        return _resolvedRaw;
    }

    [ThreadStatic] private static Engine? _convertEngine;

    /// <summary>
    /// 将结果转换为 CLR 对象（原 <see cref="JsEngine.ToClrValue"/> 语义）。
    /// </summary>
    public object? ToClr()
    {
        EnsureSuccess();
        var raw = ResolveRaw();

        if (raw.IsUndefined() || raw.IsNull())
        {
            return null;
        }

        if (raw.IsBoolean())
        {
            return raw.AsBoolean();
        }

        if (raw.IsNumber())
        {
            var num = raw.AsNumber();
            return num == Math.Floor(num) && num is >= int.MinValue and <= int.MaxValue
                ? (int)num
                : num;
        }

        if (raw.IsString())
        {
            return raw.AsString();
        }

        return ToJson();
    }

    /// <summary>
    /// 将结果按 JavaScript 真值语义转换为布尔值。
    /// </summary>
    public bool ToBoolean()
    {
        EnsureSuccess();
        var raw = ResolveRaw();

        if (raw.IsUndefined() || raw.IsNull())
        {
            return false;
        }

        if (raw.IsBoolean())
        {
            return raw.AsBoolean();
        }

        if (raw.IsNumber())
        {
            var num = raw.AsNumber();
            return !double.IsNaN(num) && num != 0;
        }

        if (raw.IsString())
        {
            return raw.AsString().Length > 0;
        }

        // 数组与对象在 JS 中均为 truthy。
        if (raw.IsArray() || raw.IsObject())
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
        var raw = ResolveRaw();

        if (raw.IsUndefined() || raw.IsNull())
        {
            return null;
        }

        if (raw.IsBoolean())
        {
            return JsonValue.Create(raw.AsBoolean());
        }

        if (raw.IsNumber())
        {
            var num = raw.AsNumber();
            if (num == Math.Floor(num) && num is >= int.MinValue and <= int.MaxValue)
            {
                return JsonValue.Create((int)num);
            }

            return JsonValue.Create(num);
        }

        if (raw.IsString())
        {
            return JsonValue.Create(raw.AsString());
        }

        try
        {
            var obj = raw.ToObject();
            return JsonSerializer.SerializeToNode(obj, JsonDefaults.Options);
        }
        catch
        {
            var str = raw.ToString();
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
        var raw = ResolveRaw();

        var targetType = typeof(T);

        if (targetType == typeof(string))
        {
            return (T?)(object?)raw.ToString();
        }

        if (targetType == typeof(bool))
        {
            return (T?)(object)ToBoolean();
        }

        if (targetType == typeof(int) || targetType == typeof(long) || targetType == typeof(double))
        {
            if (!raw.IsNumber())
            {
                return default;
            }

            var num = raw.AsNumber();
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
