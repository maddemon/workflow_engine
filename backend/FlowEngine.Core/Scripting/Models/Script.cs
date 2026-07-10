using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Scripting.Models;

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

        try
        {
            return ResolvedValue.GetValue<T>();
        }
        catch (InvalidOperationException)
        {
            return JsonSerializer.Deserialize<T>(ResolvedValue.ToJsonString(), JsonDefaults.Options);
        }
    }

    /// <summary>
    /// 创建携带运行时解析值的新 <see cref="Script"/> 实例。
    /// </summary>
    internal Script WithResolvedValue(JsonNode? value)
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
