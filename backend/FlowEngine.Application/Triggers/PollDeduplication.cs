using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;

namespace FlowEngine.Application.Triggers;

/// <summary>
/// 轮询去重辅助类。
/// </summary>
public static class PollDeduplication
{
    private const int MaxHashSetSize = 10000;

    /// <summary>
    /// 判断是否应处理该数据项。
    /// </summary>
    /// <param name="item">待处理的数据项。</param>
    /// <param name="strategy">去重策略。</param>
    /// <param name="lastPollId">上一次轮询 ID。</param>
    /// <param name="lastPollTime">上一次轮询时间。</param>
    /// <returns>是否应处理。</returns>
    public static bool ShouldProcess(DataItem item, string strategy, string? lastPollId, DateTime? lastPollTime)
    {
        if (string.IsNullOrEmpty(strategy) || strategy.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (item.Data is null)
        {
            return false;
        }

        return strategy.ToLowerInvariant() switch
        {
            "id" => ShouldProcessById(item, lastPollId),
            "timestamp" => ShouldProcessByTimestamp(item, lastPollTime),
            "hashset" => ShouldProcessByHashSet(item, lastPollId),
            _ => true,
        };
    }

    /// <summary>
    /// 处理后更新去重状态。
    /// </summary>
    /// <param name="items">已处理的数据项。</param>
    /// <param name="settings">触发器配置。</param>
    /// <returns>更新后的触发器配置。</returns>
    public static TriggerSettings UpdateState(IReadOnlyList<DataItem> items, TriggerSettings settings)
    {
        if (items.Count == 0)
        {
            return settings;
        }

        var strategy = settings.DedupStrategy;
        if (string.IsNullOrEmpty(strategy) || strategy.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return settings;
        }

        return strategy.ToLowerInvariant() switch
        {
            "id" => UpdateIdState(items, settings),
            "timestamp" => UpdateTimestampState(items, settings),
            "hashset" => UpdateHashSetState(items, settings),
            _ => settings,
        };
    }

    private static bool ShouldProcessById(DataItem item, string? lastPollId)
    {
        if (item.Data is not JsonObject obj)
        {
            return true;
        }

        if (!obj.TryGetPropertyValue("id", out var idNode) || idNode is null)
        {
            return true;
        }

        var id = idNode.ToString();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(lastPollId))
        {
            return true;
        }

        // 对于 ID 策略，我们处理 ID 大于上次轮询 ID 的数据项
        return string.Compare(id, lastPollId, StringComparison.Ordinal) > 0;
    }

    private static bool ShouldProcessByTimestamp(DataItem item, DateTime? lastPollTime)
    {
        if (!lastPollTime.HasValue)
        {
            return true;
        }

        if (item.Data is not JsonObject obj)
        {
            return true;
        }

        if (!obj.TryGetPropertyValue("timestamp", out var tsNode) || tsNode is null)
        {
            return true;
        }

        if (DateTime.TryParse(tsNode.ToString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var timestamp))
        {
            var utcTs = timestamp.Kind == DateTimeKind.Utc ? timestamp : timestamp.ToUniversalTime();
            var utcLast = lastPollTime.Value.Kind == DateTimeKind.Utc ? lastPollTime.Value : lastPollTime.Value.ToUniversalTime();
            return utcTs > utcLast;
        }

        return true;
    }

    /// <summary>
    /// 提取数据项的唯一标识。优先使用 Id 字段，回退到 JSON 内容的确定性哈希。
    /// </summary>
    /// <param name="item">数据项。</param>
    /// <returns>唯一标识，无法提取时返回空字符串。</returns>
    private static string ExtractItemKey(DataItem item)
    {
        if (item.Data is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("id", out var idNode) && idNode is not null)
            {
                var id = idNode.ToString();
                if (!string.IsNullOrEmpty(id))
                {
                    return "id:" + id;
                }
            }
        }

        if (item.Data is null)
        {
            return string.Empty;
        }

        var json = item.Data.ToJsonString();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return "hash:" + Convert.ToHexString(hashBytes, 0, 8);
    }

    /// <summary>
    /// 从 LastPollId 解析已处理唯一标识有序列表（按插入顺序保留，便于超限时淘汰最旧条目）。
    /// </summary>
    /// <param name="lastPollId">上次轮询存储的 JSON 数组。</param>
    /// <returns>保序的已处理标识列表。</returns>
    private static List<string> ParseProcessedHashSet(string? lastPollId)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(lastPollId))
        {
            return list;
        }

        try
        {
            if (JsonArray.Parse(lastPollId) is JsonArray arr)
            {
                foreach (var node in arr)
                {
                    if (node?.ToString() is { } key && !string.IsNullOrEmpty(key))
                    {
                        list.Add(key);
                    }
                }
            }
        }
        catch
        {
            // 忽略解析错误
        }

        return list;
    }

    private static bool ShouldProcessByHashSet(DataItem item, string? lastPollId)
    {
        var key = ExtractItemKey(item);
        if (string.IsNullOrEmpty(key))
        {
            return true;
        }

        var processed = ParseProcessedHashSet(lastPollId);
        return !processed.Contains(key);
    }

    private static TriggerSettings UpdateIdState(IReadOnlyList<DataItem> items, TriggerSettings settings)
    {
        var lastItem = items[^1];
        if (lastItem.Data is JsonObject obj && obj.TryGetPropertyValue("id", out var idNode) && idNode is not null)
        {
            settings.LastPollId = idNode.ToString();
        }

        settings.LastPollTime = DateTime.UtcNow;
        return settings;
    }

    private static TriggerSettings UpdateTimestampState(IReadOnlyList<DataItem> items, TriggerSettings settings)
    {
        var lastItem = items[^1];
        if (lastItem.Data is JsonObject obj && obj.TryGetPropertyValue("timestamp", out var tsNode) && tsNode is not null)
        {
            if (DateTime.TryParse(tsNode.ToString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var timestamp))
            {
                settings.LastPollTime = timestamp.Kind == DateTimeKind.Utc ? timestamp : timestamp.ToUniversalTime();
            }
        }

        return settings;
    }

    private static TriggerSettings UpdateHashSetState(IReadOnlyList<DataItem> items, TriggerSettings settings)
    {
        // HashSet 策略使用 LastPollId 存储已处理项的唯一标识有序列表（JSON 数组）
        var processed = ParseProcessedHashSet(settings.LastPollId);

        // 滑动窗口淘汰：达到上限时保留最近 90% 的记录，淘汰最旧的 10%（Code Review I-1：原全量清空导致去重失效）。
        if (processed.Count >= MaxHashSetSize)
        {
            var keepCount = (int)(MaxHashSetSize * 0.9);
            processed = processed.Skip(processed.Count - keepCount).ToList();
        }

        var seen = new HashSet<string>(processed);
        foreach (var item in items)
        {
            var key = ExtractItemKey(item);
            if (!string.IsNullOrEmpty(key) && seen.Add(key))
            {
                processed.Add(key);
            }
        }

        var arrResult = new JsonArray();
        foreach (var key in processed)
        {
            arrResult.Add(key);
        }

        settings.LastPollId = arrResult.ToJsonString();
        settings.LastPollTime = DateTime.UtcNow;
        return settings;
    }
}
