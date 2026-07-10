using System.Text.Json.Nodes;

namespace FlowEngine.Core.Scripting;

/// <summary>
/// 提供轻量级 JSON 路径导航，支持点号属性访问与数组索引。
/// 用于替代散落在节点类中的私有路径读取逻辑。
/// </summary>
public static class JsonPath
{
    /// <summary>
    /// 按路径获取 JSON 节点。路径不存在时返回 <c>null</c>。
    /// </summary>
    /// <param name="data">JSON 数据。</param>
    /// <param name="path">路径，如 <c>user.profile.age</c> 或 <c>items[0]</c>。</param>
    /// <returns>命中的 <see cref="JsonNode"/>，不存在时为 <c>null</c>。</returns>
    public static JsonNode? GetNode(JsonNode? data, string? path)
    {
        if (data is null || string.IsNullOrEmpty(path))
        {
            return null;
        }

        return Navigate(data, path.AsSpan());
    }

    /// <summary>
    /// 按路径获取 JSON 节点的字符串表示。路径不存在时返回 <c>null</c>。
    /// </summary>
    /// <param name="data">JSON 数据。</param>
    /// <param name="path">路径，如 <c>user.profile.age</c> 或 <c>items[0]</c>。</param>
    /// <returns>命中节点的字符串值，不存在时为 <c>null</c>。</returns>
    public static string? GetValue(JsonNode? data, string? path)
    {
        return GetNode(data, path)?.ToString();
    }

    private static JsonNode? Navigate(JsonNode node, ReadOnlySpan<char> path)
    {
        if (path.Length == 0) return node;

        var current = node;
        var remaining = path;

        while (remaining.Length > 0)
        {
            remaining = remaining.TrimStart('.');

            if (remaining.Length == 0) break;

            // 数组索引: [0]
            if (remaining[0] == '[')
            {
                var end = remaining.IndexOf(']');
                if (end < 0) return null;

                if (current is JsonArray arr)
                {
                    var indexStr = remaining[1..end].ToString();
                    if (int.TryParse(indexStr, out var idx) && idx >= 0 && idx < arr.Count)
                    {
                        current = arr[idx];
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }

                remaining = remaining[(end + 1)..];
                continue;
            }

            // 属性名
            var dotOrBracket = remaining.IndexOfAny('.', '[');
            var key = dotOrBracket < 0
                ? remaining.ToString()
                : remaining[..dotOrBracket].ToString();

            if (current is JsonObject obj && obj.TryGetPropertyValue(key, out var child))
            {
                current = child;
            }
            else
            {
                return null;
            }

            remaining = dotOrBracket < 0 ? [] : remaining[dotOrBracket..];
        }

        return current;
    }
}
