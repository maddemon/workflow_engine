using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;

namespace FlowEngine.Runtime.Security;

/// <summary>
/// <see cref="SecretMasker"/> 默认实现。
/// 规则：<see cref="CredentialValue"/> 仅保留 name/type；命中敏感字面量集的字符串替换为 "***"；其余透传。
/// </summary>
public sealed class SecretMasker
{
    /// <summary>
    /// 对数据批次脱敏。
    /// </summary>
    /// <param name="batch">待脱敏的数据批次。</param>
    /// <param name="sensitiveValues">敏感字面量集合。</param>
    /// <param name="deepCopy">
    /// 是否先深拷贝再脱敏。<c>true</c>（默认）不修改原数据；<c>false</c> 原地脱敏以省去拷贝开销，
    /// 调用方需确保此后不再使用原始批次。
    /// </param>
    public DataBatch MaskDataBatch(DataBatch batch, IReadOnlySet<string> sensitiveValues, bool deepCopy = true)
    {
        ArgumentNullException.ThrowIfNull(batch);

        // 注意：普通执行传入空敏感集时，本方法仍会递归遍历每个 DataItem 的 JSON，
        // 但仅凭据结构（CredentialValue）被脱敏为 {name,type}，字面量不因空集而替换。
        var sanitized = new DataBatch { Items = [] };
        foreach (var item in batch.Items)
        {
            var node = item.Data;
            if (deepCopy && node is not null)
            {
                node = node.DeepClone();
            }

            sanitized.Items.Add(new DataItem
            {
                Data = node is null ? null : MaskJsonNode(node, sensitiveValues),
                Success = item.Success,
                Error = item.Error,
                SourceIndex = item.SourceIndex,
                AttachmentId = item.AttachmentId
            });
        }

        return sanitized;
    }

    public NodeExecutionResult MaskOutput(NodeExecutionResult result, IReadOnlySet<string> sensitiveValues)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new NodeExecutionResult
        {
            Success = result.Success,
            Output = MaskDataBatch(result.Output, sensitiveValues),
            Error = result.Error,
            BranchIndex = result.BranchIndex
        };
    }

    public Dictionary<string, object> MaskParameters(
        IReadOnlyDictionary<string, object> parameters,
        IReadOnlySet<string> sensitiveValues)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var sanitized = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parameters)
        {
            sanitized[key] = MaskValue(value, sensitiveValues)!;
        }

        return sanitized;
    }

    public object? MaskValue(object? value, IReadOnlySet<string> sensitiveValues)
    {
        if (value is null)
        {
            return null;
        }

        if (value is CredentialValue credential)
        {
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = credential.Name,
                ["type"] = credential.Type
            };
        }

        if (value is string text && sensitiveValues.Contains(text))
        {
            return "***";
        }

        if (value is JsonNode jsonNode)
        {
            return MaskJsonNode(jsonNode.DeepClone(), sensitiveValues);
        }

        if (value is IDictionary<string, object> genericDict)
        {
            var sanitizedDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, item) in genericDict)
            {
                sanitizedDict[key] = MaskValue(item, sensitiveValues);
            }

            return sanitizedDict;
        }

        if (value is IDictionary nonGenericDict)
        {
            var sanitizedDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in nonGenericDict)
            {
                sanitizedDict[entry.Key?.ToString() ?? string.Empty] = MaskValue(entry.Value, sensitiveValues);
            }

            return sanitizedDict;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            var sanitizedList = new List<object?>();
            foreach (var item in enumerable)
            {
                sanitizedList.Add(MaskValue(item, sensitiveValues));
            }

            return sanitizedList;
        }

        return value;
    }

    public JsonNode? MaskJsonNode(JsonNode? node, IReadOnlySet<string> sensitiveValues)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                var sanitizedProperty = MaskJsonNode(property.Value, sensitiveValues);
                if (!ReferenceEquals(property.Value, sanitizedProperty))
                {
                    jsonObject[property.Key] = sanitizedProperty;
                }
            }

            return jsonObject;
        }

        if (node is JsonArray jsonArray)
        {
            for (var i = 0; i < jsonArray.Count; i++)
            {
                var original = jsonArray[i];
                var sanitizedItem = MaskJsonNode(original, sensitiveValues);
                if (!ReferenceEquals(original, sanitizedItem))
                {
                    jsonArray[i] = sanitizedItem;
                }
            }

            return jsonArray;
        }

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) && sensitiveValues.Contains(text))
        {
            return JsonValue.Create("***");
        }

        return node;
    }
}
