using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FlowEngine.Core.Data;

/// <summary>
/// 通用 JSON 列值转换器工厂。
/// 通过 <see cref="Create"/> 为任意 CLR 类型构造 <see cref="ValueConverter"/>,
/// 统一配合 <see cref="Attributes.JsonColumnAttribute"/> 使用。
/// </summary>
public static class JsonValueConverter
{
    /// <summary>
    /// 为指定 CLR 类型创建 JSON 值转换器。
    /// </summary>
    /// <param name="clrType">属性 CLR 类型,可为 nullable 包装类型。</param>
    /// <returns>对应 <see cref="ValueConverter"/> 实例。</returns>
    /// <exception cref="InvalidOperationException">类型无法反序列化时抛出。</exception>
    public static ValueConverter Create(Type clrType)
    {
        var converterType = typeof(JsonValueConverter<>).MakeGenericType(clrType);
        var instance = Activator.CreateInstance(converterType, JsonDefaults.Options);
        if (instance is null)
        {
            throw new InvalidOperationException($"无法为类型 {clrType} 创建 JSON 值转换器。");
        }

        return (ValueConverter)instance;
    }
}

/// <summary>
/// 强类型 JSON 值转换器,使用 <see cref="JsonDefaults.Options"/> 序列化/反序列化。
/// </summary>
/// <typeparam name="T">属性 CLR 类型。</typeparam>
public sealed class JsonValueConverter<T> : ValueConverter<T, string>
{
    /// <summary>
    /// 初始化 JSON 值转换器。
    /// </summary>
    /// <param name="options">JSON 序列化选项。</param>
    public JsonValueConverter(JsonSerializerOptions options)
        : base(
            v => Serialize(v, options),
            v => Deserialize(v, options))
    {
    }

    private static string Serialize(T value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return JsonSerializer.Serialize(value, options);
    }

    private static T Deserialize(string json, JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(json, options)
            ?? default!;
    }
}
