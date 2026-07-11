using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Registry.Converters;

/// <summary>
/// List&lt;T&gt; 与 Array 类型转换策略。
/// </summary>
internal sealed class ListConverter : IValueConverter
{
    public bool CanConvert(Type targetType)
        => IsGenericList(targetType, out _) || targetType.IsArray;

    public Task<object?> ConvertAsync(object? value, Type targetType, ParameterHydratorContext context)
    {
        object? result;

        if (IsGenericList(targetType, out var elementType))
        {
            result = ConvertToList(value!, targetType, elementType, context);
        }
        else if (targetType.IsArray)
        {
            var arrayElementType = targetType.GetElementType()!;
            var listType = typeof(List<>).MakeGenericType(arrayElementType);
            var list = ConvertToList(value!, listType, arrayElementType, context);
            if (list is not null)
            {
                var toArray = listType.GetMethod("ToArray");
                result = toArray?.Invoke(list, null);
            }
            else
            {
                result = null;
            }
        }
        else
        {
            result = null;
        }

        return Task.FromResult<object?>(result);
    }

    private static object? ConvertToList(object value, Type listType, Type elementType, ParameterHydratorContext context)
    {
        try
        {
            return value switch
            {
                JsonElement element when element.ValueKind == JsonValueKind.Array
                    => JsonSerializer.Deserialize(element.GetRawText(), listType, JsonDefaults.Options),
                string s => JsonSerializer.Deserialize(s, listType, JsonDefaults.Options),
                JsonNode node => JsonSerializer.Deserialize(node.ToJsonString(), listType, JsonDefaults.Options),
                _ when listType.IsInstanceOfType(value) => value,
                _ => ConvertEnumerableToList(value, listType, elementType)
            };
        }
        catch (Exception ex)
        {
            context.Logger?.LogWarning(ex, "列表类型 {ListType} 反序列化失败。", listType.Name);
            return null;
        }
    }

    private static object? ConvertEnumerableToList(object value, Type listType, Type elementType)
    {
        if (value is not IEnumerable enumerable || value is string)
        {
            return null;
        }

        var list = (IList?)Activator.CreateInstance(listType);
        if (list is null)
        {
            return null;
        }

        foreach (var item in enumerable)
        {
            if (item is null)
            {
                continue;
            }

            if (elementType.IsInstanceOfType(item))
            {
                list.Add(item);
            }
            else
            {
                return null;
            }
        }

        return list;
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
}
