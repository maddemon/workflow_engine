using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FlowEngine.Core.Scripting;

/// <summary>
/// 脚本值对象。包含脚本源码、语言、返回类型提示以及可选的运行时解析值。
/// </summary>
[JsonConverter(typeof(ScriptJsonConverter))]
public sealed class Script : IEquatable<Script>
{
    /// <summary>
    /// 脚本源码。
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// 脚本语言。
    /// </summary>
    public ScriptLanguage Language { get; init; } = ScriptLanguage.JavaScript;

    /// <summary>
    /// 返回类型提示，用于结果渲染与转换。
    /// </summary>
    public ScriptReturnType ReturnType { get; init; } = ScriptReturnType.Object;

    /// <summary>
    /// 运行时解析值，不参与持久化与相等性比较。
    /// </summary>
    [JsonIgnore]
    public JsonNode? ResolvedValue { get; init; }

    /// <summary>
    /// 初始化 <see cref="Script"/> 的空构造函数。
    /// </summary>
    public Script()
    {
    }

    /// <summary>
    /// 空脚本实例。
    /// </summary>
    public static Script Empty { get; } = new Script();

    /// <summary>
    /// 内部构造函数，用于 <see cref="WithResolvedValue"/> 与缓存层。
    /// </summary>
    internal Script(string source, ScriptLanguage language, ScriptReturnType returnType, JsonNode? resolvedValue = null)
    {
        Source = source;
        Language = language;
        ReturnType = returnType;
        ResolvedValue = resolvedValue;
    }

    /// <summary>
    /// 获取解析值为指定 CLR 类型。
    /// </summary>
    public T? GetResult<T>()
    {
        if (ResolvedValue is null)
        {
            return default;
        }

        // 字符串目标：将 JSON 值（含数字/布尔）按预期语义强制为字符串，
        // 避免 JsonValue 无法按 string 反序列化时静默返回 null（如数值结果参与 SwitchNode 匹配）。
        if (typeof(T) == typeof(string))
        {
            return (T?)(object?)CoerceToString(ResolvedValue);
        }

        try
        {
            return ResolvedValue.GetValue<T>();
        }
        catch (InvalidOperationException)
        {
            return JsonSerializer.Deserialize<T>(ResolvedValue.ToJsonString(), JsonDefaults.Options);
        }
    }

    private static string? CoerceToString(JsonNode node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var str))
            {
                return str;
            }

            if (value.TryGetValue<bool>(out var b))
            {
                return b.ToString();
            }

            if (value.TryGetValue<int>(out var i))
            {
                return i.ToString(CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<long>(out var l))
            {
                return l.ToString(CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<double>(out var d))
            {
                return d.ToString(CultureInfo.InvariantCulture);
            }
        }

        return node.ToJsonString();
    }

    /// <summary>
    /// 创建携带运行时解析值的新 <see cref="Script"/> 实例。
    /// </summary>
    public Script WithResolvedValue(JsonNode? value)
    {
        return new Script(Source, Language, ReturnType, value);
    }

    /// <inheritdoc />
    public bool Equals(Script? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Source == other.Source
               && Language == other.Language
               && ReturnType == other.ReturnType;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Script);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Source, Language, ReturnType);

    /// <summary>
    /// 将字符串隐式转换为 <see cref="Script"/>。
    /// </summary>
    public static implicit operator Script(string source) => new(source, ScriptLanguage.JavaScript, ScriptReturnType.Object);

    /// <inheritdoc />
    public static bool operator ==(Script? left, Script? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    /// <inheritdoc />
    public static bool operator !=(Script? left, Script? right) => !(left == right);
}
