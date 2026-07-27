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
    /// 将结果转换为 CLR 对象（JsValue → System.Text.Json 兼容类型）。
    /// </summary>
    public object? ToClr()
    {
        EnsureSuccess();

        // 数值分支优先按不丢失精度的类型返回（int/long/decimal/double）。
        if (TryGetNumber(out var number))
        {
            return number;
        }

        var raw = ResolveRaw();

        if (raw.IsUndefined() || raw.IsNull())
        {
            return null;
        }

        if (raw.IsBoolean())
        {
            return raw.AsBoolean();
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

        // 数值分支优先按不丢失精度的类型输出 JSON 数字字面量（整数不会被写成指数形式）。
        if (TryGetNumber(out var number))
        {
            return JsonValue.Create(number);
        }

        var raw = ResolveRaw();

        if (raw.IsUndefined() || raw.IsNull())
        {
            return null;
        }

        if (raw.IsBoolean())
        {
            return JsonValue.Create(raw.AsBoolean());
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
            if (!TryGetNumber(out var numObj))
            {
                return default;
            }

            if (targetType == typeof(int))
            {
                return (T?)(object)Convert.ToInt32(numObj);
            }

            if (targetType == typeof(long))
            {
                return (T?)(object)Convert.ToInt64(numObj);
            }

            return (T?)(object)Convert.ToDouble(numObj);
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

    /// <summary>
    /// 将数值结果解析为不丢失精度的 CLR 数值（int/long/decimal/double）。
    /// 已预求值的 JSON 节点按原类型直读，避免经 Jint 的 double 模型丢失大整数精度；
    /// 否则从 JsValue 读取：整数优先 long，超出 long 范围用 decimal，仅非整数用 double。
    /// </summary>
    private bool TryGetNumber(out object? number)
    {
        number = null;

        // 已预求值的 JSON 节点：直接按 System.Text.Json 原类型取值，保留大整数精度。
        if (_resolvedNode is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<JsonElement>(out var je) && je.ValueKind == JsonValueKind.Number)
            {
                number = NormalizeJsonElementNumber(je);
                return number is not null;
            }

            if (jsonValue.TryGetValue<object>(out var obj) && obj is not null)
            {
                number = NormalizeResolvedNumber(obj);
                return number is not null;
            }

            return false;
        }

        var raw = ResolveRaw();
        if (!raw.IsNumber())
        {
            return false;
        }

        var num = raw.AsNumber();
        number = num == Math.Floor(num) && !double.IsInfinity(num) && !double.IsNaN(num)
            ? NormalizeIntegral(num)
            : num;
        return true;
    }

    /// <summary>
    /// 将已确认无小数部分的整数规范化为最紧凑的 CLR 类型：
    /// int 范围内用 int（兼容既有行为），否则 long，超出 long 范围用 decimal。
    /// </summary>
    private static object NormalizeIntegral(double value)
    {
        if (value is >= int.MinValue and <= int.MaxValue)
        {
            return (int)value;
        }

        if (value is >= long.MinValue and <= long.MaxValue)
        {
            return (long)value;
        }

        return (decimal)value;
    }

    /// <summary>
    /// 将已确认无小数部分的整数（源自 JSON 节点）规范化为 int/long，保留精确值。
    /// </summary>
    private static object NormalizeIntegral(long value)
    {
        if (value is >= int.MinValue and <= int.MaxValue)
        {
            return (int)value;
        }

        return value;
    }

    /// <summary>
    /// 将已解析的 JSON 数字元素规范化为不丢失精度的 CLR 类型。
    /// 优先 int/long 保留整数精度，其次 decimal 保留金额精度，仅非整数用 double。
    /// </summary>
    private static object? NormalizeJsonElementNumber(JsonElement element)
    {
        if (element.TryGetInt64(out var l))
        {
            return NormalizeIntegral(l);
        }

        if (element.TryGetDecimal(out var d))
        {
            return d;
        }

        if (element.TryGetDouble(out var db))
        {
            return db == Math.Floor(db) && !double.IsInfinity(db) && !double.IsNaN(db)
                ? NormalizeIntegral(db)
                : db;
        }

        return null;
    }

    /// <summary>
    /// 将已预求值 JSON 节点反序列化得到的基础数值对象规范化为不丢失精度的 CLR 类型。
    /// </summary>
    private static object? NormalizeResolvedNumber(object obj) => obj switch
    {
        int or short or byte or sbyte or uint or ushort => obj,
        long l => NormalizeIntegral(l),
        ulong ul => NormalizeIntegral((long)ul),
        decimal m => m,
        double d => d == Math.Floor(d) && !double.IsInfinity(d) && !double.IsNaN(d)
            ? NormalizeIntegral(d)
            : d,
        float f => f == Math.Floor(f) && !float.IsInfinity(f) && !float.IsNaN(f)
            ? NormalizeIntegral(f)
            : f,
        JsonElement je => NormalizeJsonElementNumber(je),
        _ => null
    };

    private void EnsureSuccess()
    {
        if (!Success)
        {
            throw Error ?? new ScriptErrorException(Original, "脚本执行失败");
        }
    }
}
