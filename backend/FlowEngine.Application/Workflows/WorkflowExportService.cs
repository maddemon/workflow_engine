using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Data;
using FlowEngine.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流导出服务。
/// </summary>
public sealed class WorkflowExportService(FlowEngineDbContext dbContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>
    /// 凭据值对象中需脱敏的属性名（小写匹配）。
    /// CredentialValue.Fields 与 BinaryFields 含明文凭据，导出时必须移除（GAP-01）。
    /// </summary>
    private static readonly HashSet<string> CredentialSensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "fields",
        "binaryFields"
    };

    /// <summary>
    /// 导出单个工作流为 JSON 字符串。
    /// </summary>
    public async Task<string> ExportAsync(Guid workflowId, string exportedBy, CancellationToken cancellationToken = default)
    {
        var workflow = await dbContext.Workflows
            .FirstOrDefaultAsync(w => w.Id == workflowId, cancellationToken)
            .ConfigureAwait(false);

        if (workflow is null)
        {
            throw new NotFoundException($"工作流 {workflowId} 不存在。");
        }

        var result = MapToExportResult(workflow, exportedBy);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    /// <summary>
    /// 批量导出多个工作流为 JSON 数组字符串。
    /// </summary>
    public async Task<string> ExportBatchAsync(
        IEnumerable<Guid> ids,
        string exportedBy,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        var workflows = await dbContext.Workflows
            .Where(w => idList.Contains(w.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var missingIds = idList.Except(workflows.Select(w => w.Id)).ToList();
        if (missingIds.Count > 0)
        {
            throw new NotFoundException(
                $"以下工作流不存在：{string.Join(", ", missingIds)}。");
        }

        var results = workflows.Select(w => MapToExportResult(w, exportedBy)).ToList();
        return JsonSerializer.Serialize(results, JsonOptions);
    }

    private static WorkflowExportResult MapToExportResult(
        Core.Entities.Workflow workflow,
        string exportedBy)
    {
        var nodeDtos = workflow.Nodes.Select(n =>
        {
            // 导出前对参数做凭据脱敏，移除 CredentialValue 中的明文字段（GAP-01）。
            var sanitized = SanitizeParameters(n.Parameters);
            var dto = WorkflowMapper.ToDto(n, n.Id.ToString());
            return new NodeDefinitionDto
            {
                Id = dto.Id,
                TypeName = dto.TypeName,
                Name = dto.Name,
                Parameters = sanitized,
                Ports = dto.Ports,
                PositionX = dto.PositionX,
                PositionY = dto.PositionY,
                IsEntry = dto.IsEntry,
                RetryPolicy = dto.RetryPolicy,
                ErrorStrategy = dto.ErrorStrategy,
                Timeout = dto.Timeout,
            };
        }).ToList();

        var connectionDtos = workflow.Connections.Select(c =>
            WorkflowMapper.ToDto(c, c.Id.ToString(), c.SourceNodeId.ToString(), c.TargetNodeId.ToString())).ToList();

        return new WorkflowExportResult
        {
            Name = workflow.Name,
            Version = workflow.Version,
            Nodes = nodeDtos,
            Connections = connectionDtos,
            ExportedAt = DateTime.UtcNow,
            ExportedBy = exportedBy,
            StyleSettings = workflow.StyleSettings is not null
                ? JsonSerializer.SerializeToElement(workflow.StyleSettings, JsonOptions)
                    .Deserialize<Dictionary<string, object?>>(JsonOptions)
                : null,
        };
    }

    /// <summary>
    /// 递归扫描参数字典，识别 CredentialValue 结构并移除明文值（GAP-01）。
    /// CredentialValue 含 fields/binaryFields 属性，导出时移除这两个属性，
    /// 保留 name/type 等引用信息，确保凭据明文不出现在导出 JSON 中。
    /// </summary>
    private static Dictionary<string, object> SanitizeParameters(Dictionary<string, object> parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return [];
        }

        var serialized = JsonSerializer.SerializeToNode(parameters, JsonOptions);
        if (serialized is JsonObject root)
        {
            SanitizeNode(root);
        }

        return serialized?.Deserialize<Dictionary<string, object>>(JsonOptions) ?? [];
    }

    /// <summary>
    /// 递归遍历 JsonNode，对含 fields/binaryFields 属性的对象移除这些属性。
    /// </summary>
    private static void SanitizeNode(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            // 先递归子节点（避免在遍历中修改集合）。
            var children = obj.ToList();
            foreach (var (key, child) in children)
            {
                SanitizeNode(child);
            }

            // 若对象本身含凭据敏感属性，则移除这些属性。
            if (ContainsCredentialSensitiveKey(obj))
            {
                var keysToRemove = obj
                    .Select(kvp => kvp.Key)
                    .Where(k => CredentialSensitiveKeys.Contains(k))
                    .ToList();
                foreach (var key in keysToRemove)
                {
                    obj.Remove(key);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                SanitizeNode(item);
            }
        }
    }

    private static bool ContainsCredentialSensitiveKey(JsonObject obj)
    {
        foreach (var kvp in obj)
        {
            if (CredentialSensitiveKeys.Contains(kvp.Key))
            {
                return true;
            }
        }

        return false;
    }
}
