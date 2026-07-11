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
}
