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
        var matching = new List<JsonDocument>();

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

                matching.Add(doc);
            }
        }

        matching.Sort((a, b) =>
        {
            var tsA = GetTimestamp(a);
            var tsB = GetTimestamp(b);
            return tsB.CompareTo(tsA);
        });

        var total = matching.Count;
        var paged = matching
            .Skip(parameters.Offset)
            .Take(parameters.Limit)
            .ToList();

        if (paged.Count < matching.Count)
        {
            foreach (var doc in matching.Skip(parameters.Offset + parameters.Limit))
            {
                doc.Dispose();
            }
        }

        return new AuditQueryResult { Events = paged, Total = total };
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
