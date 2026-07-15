using System.Text.Json;
using FlowEngine.Application.Audit;

namespace FlowEngine.Infrastructure.Audit;

/// <summary>
/// 审计日志读取器，从 NDJSON 文件读取审计事件。
/// </summary>
public sealed class AuditLogReader : IAuditLogReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _logDirectory;

    /// <summary>
    /// 初始化审计日志读取器。
    /// </summary>
    /// <param name="logDirectory">日志目录路径。</param>
    public AuditLogReader(string logDirectory)
    {
        _logDirectory = logDirectory;
    }

    /// <inheritdoc />
    public async Task<AuditQueryResult> QueryAsync(
        AuditQueryParameters parameters,
        CancellationToken cancellationToken = default)
    {
        // 流式过滤 + 最小堆 Top-N：仅保留 Top (Offset+Limit) 条，避免全量加载内存。
        var capacity = parameters.Offset + parameters.Limit;
        var totalMatched = 0;

        // 最小堆：堆顶为当前保留中时间戳最小（最旧）的记录，新记录时间戳更大时替换堆顶。
        var heap = new PriorityQueue<JsonDocument, DateTime>(capacity + 1);

        var files = GetLogFiles(parameters.From, parameters.To);

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await foreach (var line in File.ReadLinesAsync(file, cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JsonDocument? doc;
                try
                {
                    doc = JsonDocument.Parse(line);
                }
                catch
                {
                    continue;
                }

                if (!MatchesFilter(doc, parameters))
                {
                    doc.Dispose();
                    continue;
                }

                totalMatched++;
                var timestamp = GetTimestamp(doc);

                if (heap.Count < capacity)
                {
                    heap.Enqueue(doc, timestamp);
                }
                else if (heap.TryPeek(out _, out var oldestTs) && timestamp > oldestTs)
                {
                    // 新记录比堆顶（最旧）更新，替换堆顶。
                    var evicted = heap.Dequeue();
                    evicted.Dispose();
                    heap.Enqueue(doc, timestamp);
                }
                else
                {
                    doc.Dispose();
                }
            }
        }

        // 从堆中按时间降序取出所有记录。
        var allRetained = new List<(JsonDocument Doc, DateTime Ts)>(heap.Count);
        while (heap.Count > 0)
        {
            heap.TryDequeue(out var doc, out var ts);
            if (doc is not null)
            {
                allRetained.Add((doc, ts));
            }
        }

        allRetained.Sort((a, b) => b.Ts.CompareTo(a.Ts));

        // 应用分页：跳过 Offset，取 Limit 条。
        var paged = allRetained
            .Skip(parameters.Offset)
            .Take(parameters.Limit)
            .Select(x => x.Doc)
            .ToList();

        // 释放未被分页选中的保留记录。
        for (var i = 0; i < allRetained.Count; i++)
        {
            if (i < parameters.Offset || i >= parameters.Offset + parameters.Limit)
            {
                allRetained[i].Doc.Dispose();
            }
        }

        return new AuditQueryResult { Events = paged, Total = totalMatched };
    }

    private IEnumerable<string> GetLogFiles(DateTime? from, DateTime? to)
    {
        if (!Directory.Exists(_logDirectory))
        {
            return [];
        }

        var files = Directory.GetFiles(_logDirectory, "audit-*.ndjson")
            .OrderByDescending(f => f);

        if (from.HasValue || to.HasValue)
        {
            var fromDate = (from ?? DateTime.MinValue).Date;
            var toDate = (to ?? DateTime.MaxValue).Date;

            files = files.Where(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f);
                var datePart = name.Replace("audit-", string.Empty);
                if (DateTime.TryParse(datePart, out var fileDate))
                {
                    return fileDate >= fromDate && fileDate <= toDate;
                }
                return true;
            }).OrderDescending();
        }

        return files;
    }

    private static bool MatchesFilter(JsonDocument doc, AuditQueryParameters parameters)
    {
        var root = doc.RootElement;

        if (!string.IsNullOrEmpty(parameters.EventType))
        {
            if (!root.TryGetProperty("eventType", out var et) ||
                !string.Equals(et.GetString(), parameters.EventType, StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (parameters.From.HasValue || parameters.To.HasValue)
        {
            if (root.TryGetProperty("timestamp", out var ts) &&
                ts.TryGetDateTime(out var timestamp))
            {
                if (parameters.From.HasValue && timestamp < parameters.From.Value)
                {
                    return false;
                }
                if (parameters.To.HasValue && timestamp > parameters.To.Value)
                {
                    return false;
                }
            }
        }

        if (!string.IsNullOrEmpty(parameters.ResourceType))
        {
            if (!root.TryGetProperty("resourceType", out var rt) ||
                !string.Equals(rt.GetString(), parameters.ResourceType, StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (parameters.ResourceId.HasValue)
        {
            if (!root.TryGetProperty("resourceId", out var rid) ||
                !Guid.TryParse(rid.GetString(), out var id) ||
                id != parameters.ResourceId.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static DateTime GetTimestamp(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("timestamp", out var ts) &&
            ts.TryGetDateTime(out var dt))
        {
            return dt;
        }
        return DateTime.MinValue;
    }
}
