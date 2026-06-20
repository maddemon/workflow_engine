using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Registry;

/// <summary>
/// 将 <c>resolvedValues</c> 字典赋值到节点实例属性上。
/// </summary>
public sealed class ParameterHydrator
{
    private readonly ICredentialAccessor? _credentialAccessor;
    private readonly ILogger<ParameterHydrator>? _logger;

    /// <summary>
    /// 初始化 Hydrator。
    /// </summary>
    /// <param name="credentialAccessor">凭据访问器（可选，用于 <see cref="CredentialValue"/> 属性）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public ParameterHydrator(ICredentialAccessor? credentialAccessor = null,
        ILogger<ParameterHydrator>? logger = null)
    {
        _credentialAccessor = credentialAccessor;
        _logger = logger;
    }

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
                _logger?.LogWarning(ex, "ParameterHydrator: 属性 {PropertyName} 赋值失败", property.Name);
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

        if (underlying == typeof(string))
        {
            return ConvertToString(value);
        }

        if (underlying == typeof(bool))
        {
            return ConvertToBool(value);
        }

        if (underlying == typeof(int))
        {
            return ConvertToInt(value);
        }

        if (underlying == typeof(long))
        {
            return ConvertToLong(value);
        }

        if (underlying == typeof(double))
        {
            return ConvertToDouble(value);
        }

        if (underlying == typeof(float))
        {
            return ConvertToFloat(value);
        }

        if (underlying.IsEnum)
        {
            return ConvertToEnum(value, underlying);
        }

        if (typeof(JsonObject).IsAssignableFrom(underlying))
        {
            return ConvertToJsonObject(value);
        }

        if (typeof(JsonNode).IsAssignableFrom(underlying))
        {
            return ConvertToJsonNode(value);
        }

        if (underlying == typeof(CredentialValue))
        {
            return await ConvertToCredentialAsync(value).ConfigureAwait(false);
        }

        if (IsGenericList(underlying, out var elementType))
        {
            return ConvertToList(value, underlying, elementType);
        }

        if (underlying.IsArray)
        {
            var listType = typeof(List<>).MakeGenericType(underlying.GetElementType()!);
            var list = ConvertToList(value, listType, underlying.GetElementType()!);
            if (list is not null)
            {
                var toArray = listType.GetMethod("ToArray");
                return toArray?.Invoke(list, null);
            }

            return null;
        }

        if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset))
        {
            return ConvertToDateTime(value, underlying);
        }

        if (underlying == typeof(Uri))
        {
            var str = ConvertToString(value);
            return str is not null ? new Uri(str, UriKind.RelativeOrAbsolute) : null;
        }

        if (underlying.IsGenericType
            && underlying.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            return ConvertToDictionary(value, underlying);
        }

        try
        {
            return Convert.ChangeType(value, underlying);
        }
        catch
        {
            return null;
        }
    }

    private static string? ConvertToString(object value)
    {
        return value switch
        {
            string s => s,
            JsonNode node => node.ToJsonString(),
            JsonElement element => element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : element.GetRawText(),
            _ => value.ToString()
        };
    }

    private static bool? ConvertToBool(object value)
    {
        return value switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var result) ? result : s != "0",
            int i => i != 0,
            long l => l != 0,
            double d => d != 0,
            JsonElement element => element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(element.GetString(), out var r) && r,
                JsonValueKind.Number => element.GetInt32() != 0,
                _ => false
            },
            _ => false
        };
    }

    private static int ConvertToInt(object value)
    {
        return value switch
        {
            int i => i,
            long l => ClampToInt(l),
            double d => ClampToInt(d),
            float f => ClampToInt(f),
            string s => int.TryParse(s, out var r) ? r : 0,
            JsonElement element => element.ValueKind == JsonValueKind.Number
                ? ClampToInt(element.GetDouble())
                : int.TryParse(element.GetString(), out var r) ? r : 0,
            _ => Convert.ToInt32(value)
        };
    }

    private static long ConvertToLong(object value)
    {
        return value switch
        {
            long l => l,
            int i => i,
            double d => (long)Math.Clamp(d, long.MinValue, long.MaxValue),
            string s => long.TryParse(s, out var r) ? r : 0,
            JsonElement element => element.ValueKind == JsonValueKind.Number
                ? (long)element.GetDouble()
                : long.TryParse(element.GetString(), out var r) ? r : 0,
            _ => Convert.ToInt64(value)
        };
    }

    private static double ConvertToDouble(object value)
    {
        return value switch
        {
            double d => d,
            int i => i,
            long l => l,
            float f => f,
            string s => double.TryParse(s, out var r) ? r : 0,
            JsonElement element => element.ValueKind == JsonValueKind.Number
                ? element.GetDouble()
                : double.TryParse(element.GetString(), out var r) ? r : 0,
            _ => Convert.ToDouble(value)
        };
    }

    private static float ConvertToFloat(object value)
    {
        return value switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            string s => float.TryParse(s, out var r) ? r : 0,
            JsonElement element => element.ValueKind == JsonValueKind.Number
                ? (float)element.GetDouble()
                : float.TryParse(element.GetString(), out var r) ? r : 0,
            _ => Convert.ToSingle(value)
        };
    }

    private static object? ConvertToEnum(object value, Type enumType)
    {
        try
        {
            return value switch
            {
                string s => Enum.Parse(enumType, s, ignoreCase: true),
                int i => Enum.ToObject(enumType, i),
                long l => Enum.ToObject(enumType, l),
                JsonElement element when element.ValueKind == JsonValueKind.String
                    => Enum.Parse(enumType, element.GetString()!, ignoreCase: true),
                JsonElement element when element.ValueKind == JsonValueKind.Number
                    => Enum.ToObject(enumType, element.GetInt32()),
                _ => Enum.Parse(enumType, value.ToString()!, ignoreCase: true)
            };
        }
        catch
        {
            return Enum.GetValues(enumType).GetValue(0);
        }
    }

    private static JsonObject? ConvertToJsonObject(object value)
    {
        return value switch
        {
            JsonObject obj => obj,
            JsonNode node => node is JsonObject jo ? jo : null,
            string s when !string.IsNullOrWhiteSpace(s) => JsonNode.Parse(s)?.AsObject(),
            JsonElement element => element.ValueKind == JsonValueKind.Object
                ? JsonObject.Create(element)
                : null,
            _ => null
        };
    }

    private static JsonNode? ConvertToJsonNode(object value)
    {
        return value switch
        {
            JsonNode node => node,
            string s => JsonNode.Parse(s),
            JsonElement element => JsonNode.Parse(element.GetRawText()),
            _ => null
        };
    }

    private async Task<CredentialValue?> ConvertToCredentialAsync(object value)
    {
        if (_credentialAccessor is null)
        {
            return null;
        }

        if (value is string credentialIdStr && Guid.TryParse(credentialIdStr, out var credentialId))
        {
            try
            {
                return await _credentialAccessor.GetCredentialAsync(credentialId, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "凭据 {CredentialId} 解析失败", credentialIdStr);
                return null;
            }
        }

        return null;
    }

    private static object? ConvertToList(object value, Type listType, Type elementType)
    {
        try
        {
            return value switch
            {
                JsonElement element when element.ValueKind == JsonValueKind.Array
                    => JsonSerializer.Deserialize(element.GetRawText(), listType),
                string s => JsonSerializer.Deserialize(s, listType),
                JsonNode node => JsonSerializer.Deserialize(node.ToJsonString(), listType),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static object? ConvertToDateTime(object value, Type targetType)
    {
        var str = ConvertToString(value);
        if (str is null)
        {
            return null;
        }

        if (targetType == typeof(DateTimeOffset))
        {
            return DateTimeOffset.TryParse(str, out var dto) ? dto : null;
        }

        return DateTime.TryParse(str, out var dt) ? dt : null;
    }

    private static object? ConvertToDictionary(object value, Type dictType)
    {
        try
        {
            return value switch
            {
                JsonElement element => JsonSerializer.Deserialize(element.GetRawText(), dictType),
                string s => JsonSerializer.Deserialize(s, dictType),
                JsonNode node => JsonSerializer.Deserialize(node.ToJsonString(), dictType),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsGenericList(Type type, out Type elementType)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            elementType = type.GetGenericArguments()[0];
            return true;
        }

        elementType = null!;
        return false;
    }

    private static int ClampToInt(long value)
    {
        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    private static int ClampToInt(double value)
    {
        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    private static int ClampToInt(float value)
    {
        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }
}
