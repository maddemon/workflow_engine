using System.ComponentModel.DataAnnotations;
using System.Reflection;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Scripting;
using FlowEngine.Runtime.Registry.Converters;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Registry;

/// <summary>
/// 将 <c>resolvedValues</c> 字典赋值到节点实例属性上。
/// </summary>
/// <remarks>
/// 初始化 Hydrator。
/// </remarks>
/// <param name="credentialAccessor">凭据访问器（可选，用于 <see cref="CredentialValue"/> 属性）。</param>
/// <param name="logger">日志记录器（可选）。</param>
public sealed class ParameterHydrator(ICredentialAccessor? credentialAccessor = null, ILogger<ParameterHydrator>? logger = null)
{
    private readonly ParameterHydratorContext _context = new(credentialAccessor, logger);

    // 精确类型匹配：string/bool/int/long/double/float/CredentialValue/Script/DateTime/DateTimeOffset/Uri
    private readonly Dictionary<Type, IValueConverter> _converters = new()
    {
        [typeof(string)] = new StringConverter(),
        [typeof(bool)] = new BoolConverter(),
        [typeof(int)] = new NumericConverter(),
        [typeof(long)] = new NumericConverter(),
        [typeof(double)] = new NumericConverter(),
        [typeof(float)] = new NumericConverter(),
        [typeof(CredentialValue)] = new CredentialConverter(),
        [typeof(Script)] = new ScriptConverter(),
        [typeof(DateTime)] = new DateTimeConverter(),
        [typeof(DateTimeOffset)] = new DateTimeConverter(),
        [typeof(Uri)] = new UriConverter(),
    };

    // 泛型/可分配类型：按顺序匹配（enum → JsonObject/JsonNode → List<T>/Array → Dictionary<,>）
    private readonly List<IValueConverter> _genericConverters =
    [
        new EnumConverter(),
        new JsonConverter(),
        new ListConverter(),
        new DictionaryConverter(),
    ];

    private readonly FallbackConverter _fallbackConverter = new();

    /// <summary>
    /// 将已解析的参数值赋值到节点实例的对应属性上。
    /// </summary>
    /// <param name="instance">节点实例。</param>
    /// <param name="resolvedValues">已解析的参数（camelCase 键）。</param>
    public async Task HydrateAsync(INodeType instance, IReadOnlyDictionary<string, object> resolvedValues)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(resolvedValues);

        var type = instance.GetType();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod is null || property.GetMethod is null)
            {
                continue;
            }

            if (property.GetCustomAttribute<IgnoreParameterAttribute>() is not null)
            {
                continue;
            }

            if (property.DeclaringType == typeof(INodeType))
            {
                continue;
            }

            if (property.Name == nameof(INodeType.Ports))
            {
                continue;
            }

            var camelName = ParameterDiscoverer.ToCamelCase(property.Name);
            if (!resolvedValues.TryGetValue(camelName, out var value))
            {
                continue;
            }

            try
            {
                var converted = await ConvertValueAsync(value, property.PropertyType, property).ConfigureAwait(false);

                // 绑定期校验（§4.3）：在写入属性前对数值类型 clamp 到 [Range]，
                // [Required] 仅记录 warning 不抛异常，以保持现有节点既有行为优先。
                if (converted is not null)
                {
                    var rangeAttr = property.GetCustomAttribute<RangeAttribute>();
                    if (rangeAttr is not null)
                    {
                        converted = ClampToRange(converted, property.PropertyType, rangeAttr);
                    }

                    var requiredAttr = property.GetCustomAttribute<RequiredAttribute>();
                    if (requiredAttr is not null && IsMissingForRequired(converted))
                    {
                        logger?.LogWarning("ParameterHydrator: 属性 {PropertyName} 标注 [Required] 但值为空，已忽略", property.Name);
                    }
                }

                // 跳过非可空值类型赋 null，否则一律写入（包括可空值类型赋 null）
                if (converted is null && property.PropertyType.IsValueType
                    && Nullable.GetUnderlyingType(property.PropertyType) is null)
                {
                    continue;
                }
                property.SetValue(instance, converted);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "ParameterHydrator: 属性 {PropertyName} 赋值失败", property.Name);
            }
        }
    }

    private async Task<object?> ConvertValueAsync(object? value, Type targetType, PropertyInfo property)
    {
        if (value is null)
        {
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying.IsAssignableFrom(value.GetType()))
        {
            return value;
        }

        if (_converters.TryGetValue(underlying, out var converter))
        {
            return await converter.ConvertAsync(value, underlying, _context).ConfigureAwait(false);
        }

        foreach (var gc in _genericConverters)
        {
            if (gc.CanConvert(underlying))
            {
                return await gc.ConvertAsync(value, underlying, _context).ConfigureAwait(false);
            }
        }

        return await _fallbackConverter.ConvertAsync(value, underlying, _context).ConfigureAwait(false);
    }

    /// <summary>
    /// 将数值类型的值 clamp 到 <see cref="RangeAttribute"/> 指定的 [Minimum, Maximum] 区间。
    /// 仅对 int/long/double/float（含其可空形式）生效；非数值类型原样返回。
    /// </summary>
    /// <param name="value">已转换的属性值（非 null）。</param>
    /// <param name="propertyType">属性声明类型（可能为可空值类型）。</param>
    /// <param name="range">[Range] 特性。</param>
    /// <returns>clamp 后的数值，或原值（非数值/无法转换上下界时）。</returns>
    private static object? ClampToRange(object? value, Type propertyType, RangeAttribute range)
    {
        if (value is null)
        {
            return value;
        }

        var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (underlying != typeof(int) && underlying != typeof(long) && underlying != typeof(double) && underlying != typeof(float))
        {
            return value;
        }

        if (!TryConvertNumeric(range.Minimum, underlying, out var min) || !TryConvertNumeric(range.Maximum, underlying, out var max))
        {
            return value;
        }

        var comparable = (IComparable)value;
        if (comparable.CompareTo(min) < 0)
        {
            return min;
        }

        if (comparable.CompareTo(max) > 0)
        {
            return max;
        }

        return value;
    }

    /// <summary>
    /// 将 <see cref="RangeAttribute"/> 的 Minimum/Maximum（多为 int）安全转换为目标数值类型。
    /// </summary>
    private static bool TryConvertNumeric(object? raw, Type targetType, out object? result)
    {
        try
        {
            result = Convert.ChangeType(raw, targetType);
            return true;
        }
        catch (Exception)
        {
            result = 0;
            return false;
        }
    }

    /// <summary>
    /// 判定值是否视为 [Required] 缺失：null 或空白字符串视为缺失；枚举默认值不视为缺失（避免过度严格）。
    /// </summary>
    private static bool IsMissingForRequired(object? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value is string s && string.IsNullOrWhiteSpace(s))
        {
            return true;
        }

        return false;
    }
}
