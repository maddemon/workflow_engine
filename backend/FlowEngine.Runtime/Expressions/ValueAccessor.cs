using System.Collections;
using System.Text.Json.Nodes;

namespace FlowEngine.Runtime.Expressions;

/// <summary>
/// 通用值访问器，支持对象属性、字典键、JsonNode 路径访问。
/// </summary>
internal static class ValueAccessor
{
    /// <summary>
    /// 获取目标对象的成员或索引值。
    /// </summary>
    /// <param name="target">目标对象。</param>
    /// <param name="memberName">成员名称或字典键。</param>
    /// <param name="availableFields">输出可用字段列表。</param>
    /// <returns>成员值。</returns>
    /// <exception cref="ExpressionEvaluationException">成员不存在时抛出。</exception>
    public static object? GetMember(object? target, string memberName, out IReadOnlyList<string> availableFields)
    {
        availableFields = [];

        if (target is null)
        {
            throw CreateFieldNotFound(memberName, [], "目标对象为 null。");
        }

        if (target is JsonObject jsonObject)
        {
            availableFields = jsonObject.Select(x => x.Key).ToList();
            if (jsonObject.TryGetPropertyValue(memberName, out var value))
            {
                return NormalizeJsonValue(value);
            }

            throw CreateFieldNotFound(memberName, availableFields, $"JSON 对象中不存在字段 '{memberName}'。");
        }

        if (target is JsonNode jsonNode && jsonNode is not JsonObject)
        {
            throw CreateFieldNotFound(memberName, [], $"类型 '{jsonNode.GetType().Name}' 不支持成员访问。");
        }

        if (target is IDictionary dictionary)
        {
            var keys = dictionary.Keys.Cast<object>().Select(x => x.ToString() ?? string.Empty).ToList();
            availableFields = keys;
            if (dictionary.Contains(memberName))
            {
                return dictionary[memberName];
            }

            throw CreateFieldNotFound(memberName, availableFields, $"字典中不存在键 '{memberName}'。");
        }

        if (target is IDictionary<string, object> genericDictionary)
        {
            availableFields = genericDictionary.Keys.ToList();
            if (genericDictionary.TryGetValue(memberName, out var value))
            {
                return value;
            }

            throw CreateFieldNotFound(memberName, availableFields, $"字典中不存在键 '{memberName}'。");
        }

        throw new ExpressionEvaluationException(new ExpressionError
        {
            Type = ExpressionErrorType.TypeMismatch,
            Expression = memberName,
            Reason = $"类型 '{target.GetType().Name}' 不支持成员访问。"
        });
    }

    /// <summary>
    /// 对目标对象按索引取值。
    /// </summary>
    /// <param name="target">目标对象。</param>
    /// <param name="index">索引值。</param>
    /// <returns>索引值。</returns>
    /// <exception cref="ExpressionEvaluationException">索引无效时抛出。</exception>
    public static object? GetIndex(object? target, object? index)
    {
        if (target is null)
        {
            throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.TypeMismatch,
                Reason = "目标对象为 null，无法使用索引。"
            });
        }

        if (target is JsonArray jsonArray)
        {
            var intIndex = Convert.ToInt32(index);
            if (intIndex < 0 || intIndex >= jsonArray.Count)
            {
                throw new ExpressionEvaluationException(new ExpressionError
                {
                    Type = ExpressionErrorType.TypeMismatch,
                    Reason = $"数组索引 {intIndex} 越界。"
                });
            }

            return NormalizeJsonValue(jsonArray[intIndex]);
        }

        if (target is IList list)
        {
            var intIndex = Convert.ToInt32(index);
            return list[intIndex];
        }

        if (target is Array array)
        {
            var intIndex = Convert.ToInt32(index);
            return array.GetValue(intIndex);
        }

        if (target is IDictionary dictionary)
        {
            return dictionary[index!];
        }

        if (target is IDictionary<string, object> genericDictionary && index is string stringIndex)
        {
            return genericDictionary[stringIndex];
        }

        throw new ExpressionEvaluationException(new ExpressionError
        {
            Type = ExpressionErrorType.TypeMismatch,
            Reason = $"类型 '{target.GetType().Name}' 不支持索引访问。"
        });
    }

    private static object? NormalizeJsonValue(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<int>(out var intValue))
            {
                return intValue;
            }

            if (jsonValue.TryGetValue<double>(out var doubleValue))
            {
                return doubleValue;
            }

            if (jsonValue.TryGetValue<bool>(out var boolValue))
            {
                return boolValue;
            }

            return jsonValue.ToString();
        }

        return node;
    }

    private static ExpressionEvaluationException CreateFieldNotFound(
        string fieldName,
        IReadOnlyList<string> availableFields,
        string reason)
    {
        return new ExpressionEvaluationException(new ExpressionError
        {
            Type = ExpressionErrorType.FieldNotFound,
            Expression = fieldName,
            Reason = reason,
            AvailableFields = availableFields
        });
    }
}
