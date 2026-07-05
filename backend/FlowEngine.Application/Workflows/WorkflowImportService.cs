using System.Text.Json;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流导入服务。
/// </summary>
public sealed class WorkflowImportService(
    FlowEngineDbContext dbContext,
    INodeRegistry nodeRegistry,
    IEventBus eventBus,
    AuditEventFactory auditFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // 导入校验器，复用已注入的节点注册中心（GAP-15）。
    private readonly WorkflowValidator _validator = new(nodeRegistry);

    /// <summary>
    /// 导入单个工作流。
    /// </summary>
    public async Task<ImportResult> ImportAsync(
        string json,
        Guid? projectId,
        string importedBy,
        CancellationToken cancellationToken = default)
    {
        WorkflowExportResult? exportResult;
        try
        {
            exportResult = JsonSerializer.Deserialize<WorkflowExportResult>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return new ImportResult
            {
                Success = false,
                Errors = [new ImportError { ErrorType = "Validation", Message = $"JSON 解析失败：{ex.Message}" }],
            };
        }

        if (exportResult is null)
        {
            return new ImportResult
            {
                Success = false,
                Errors = [new ImportError { ErrorType = "Validation", Message = "导入数据为空。" }],
            };
        }

        return await ImportSingleAsync(exportResult, projectId, importedBy, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 批量导入工作流。
    /// </summary>
    public async Task<BatchImportResult> ImportBatchAsync(
        string json,
        Guid? projectId,
        string importedBy,
        CancellationToken cancellationToken = default)
    {
        List<WorkflowExportResult>? exportResults;
        try
        {
            exportResults = JsonSerializer.Deserialize<List<WorkflowExportResult>>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return new BatchImportResult
            {
                FailureCount = 1,
                Results =
                [
                    new ImportResult
                    {
                        Success = false,
                        Errors = [new ImportError { ErrorType = "Validation", Message = $"JSON 解析失败：{ex.Message}" }],
                    },
                ],
            };
        }

        if (exportResults is null || exportResults.Count == 0)
        {
            return new BatchImportResult
            {
                FailureCount = 1,
                Results =
                [
                    new ImportResult
                    {
                        Success = false,
                        Errors = [new ImportError { ErrorType = "Validation", Message = "导入数据为空。" }],
                    },
                ],
            };
        }

        var results = new List<ImportResult>();
        var successCount = 0;
        var failureCount = 0;

        foreach (var item in exportResults)
        {
            var result = await ImportSingleAsync(item, projectId, importedBy, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            if (result.Success) successCount++;
            else failureCount++;
        }

        return new BatchImportResult
        {
            SuccessCount = successCount,
            FailureCount = failureCount,
            Results = results,
        };
    }

    private async Task<ImportResult> ImportSingleAsync(
        WorkflowExportResult exportResult,
        Guid? projectId,
        string importedBy,
        CancellationToken cancellationToken)
    {
        var errors = ValidateExportData(exportResult);
        if (errors.Count > 0)
        {
            return new ImportResult
            {
                Success = false,
                WorkflowName = exportResult.Name,
                Errors = errors,
            };
        }

        var nodeIdMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var nodes = exportResult.Nodes.Select(n =>
        {
            var node = new NodeDefinition
            {
                Id = Guid.NewGuid(),
                TypeName = n.TypeName,
                Name = n.Name,
                Parameters = n.Parameters,
                Ports = n.Ports,
                PositionX = n.PositionX,
                PositionY = n.PositionY,
                IsEntry = n.IsEntry,
                RetryPolicy = n.RetryPolicy,
                ErrorStrategy = n.ErrorStrategy,
                Timeout = n.Timeout,
            };

            if (!string.IsNullOrEmpty(n.Id))
            {
                nodeIdMap[n.Id] = node.Id;
            }

            return node;
        }).ToList();

        var connections = exportResult.Connections.Select(c =>
        {
            var sourceGuid = nodeIdMap.TryGetValue(c.SourceNodeId, out var s) ? s : Guid.Empty;
            var targetGuid = nodeIdMap.TryGetValue(c.TargetNodeId, out var t) ? t : Guid.Empty;

            return new Connection
            {
                Id = Guid.NewGuid(),
                SourceNodeId = sourceGuid,
                SourcePortName = c.SourcePortName,
                TargetNodeId = targetGuid,
                TargetPortName = c.TargetPortName,
                Condition = c.Condition,
            };
        }).ToList();

        var workflow = new Workflow
        {
            ProjectId = projectId,
            Name = exportResult.Name,
            CreatedBy = importedBy,
            IsActive = true,
            Nodes = nodes,
            Connections = connections,
        };

        // 导入时调用 WorkflowValidator 校验端口方向、必填参数、循环依赖等合法性（GAP-15）。
        var validationResult = _validator.Validate(workflow);
        if (!validationResult.IsValid)
        {
            return new ImportResult
            {
                Success = false,
                WorkflowName = exportResult.Name,
                Errors = validationResult.Errors
                    .Select(e => new ImportError { ErrorType = "Validation", Message = e })
                    .ToList(),
            };
        }

        if (dbContext is not null)
        {
            dbContext.Workflows.Add(workflow);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (eventBus is not null && auditFactory is not null)
        {
            await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                AuditEventTypes.WorkflowCreated,
                "Workflow",
                workflow.Id,
                new Dictionary<string, object> { ["name"] = workflow.Name, ["imported"] = true }),
                cancellationToken).ConfigureAwait(false);

            await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                AuditEventTypes.ImportPerformed,
                "Workflow",
                workflow.Id,
                new Dictionary<string, object> { ["importedBy"] = importedBy, ["name"] = workflow.Name }),
                cancellationToken).ConfigureAwait(false);
        }

        return new ImportResult
        {
            Success = true,
            WorkflowId = workflow.Id,
            WorkflowName = workflow.Name,
        };
    }

    private List<ImportError> ValidateExportData(WorkflowExportResult exportResult)
    {
        var errors = new List<ImportError>();

        if (string.IsNullOrWhiteSpace(exportResult.Name))
        {
            errors.Add(new ImportError { ErrorType = "Validation", Message = "工作流名称不能为空。" });
        }

        var registeredTypes = nodeRegistry.GetDescriptors()
            .Select(d => d.TypeName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in exportResult.Nodes)
        {
            if (string.IsNullOrEmpty(node.Id))
            {
                errors.Add(new ImportError
                {
                    ErrorType = "Validation",
                    NodeId = node.Id,
                    Message = $"节点 '{node.Name}' 缺少 ID。",
                });
                continue;
            }

            nodeIds.Add(node.Id);

            if (!registeredTypes.Contains(node.TypeName))
            {
                errors.Add(new ImportError
                {
                    ErrorType = "NodeNotFound",
                    NodeId = node.Id,
                    Message = $"节点类型 '{node.TypeName}' 不存在。",
                });
                continue;
            }

            var descriptor = nodeRegistry.GetDescriptors()
                .First(d => d.TypeName.Equals(node.TypeName, StringComparison.OrdinalIgnoreCase));

            foreach (var port in node.Ports)
            {
                var portDef = descriptor.Ports.FirstOrDefault(p =>
                    p.Name.Equals(port.Name, StringComparison.OrdinalIgnoreCase));

                if (portDef is null)
                {
                    errors.Add(new ImportError
                    {
                        ErrorType = "PortNotFound",
                        NodeId = node.Id,
                        Message = $"节点 '{node.Name}' 的端口 '{port.Name}' 在类型 '{node.TypeName}' 中不存在。",
                    });
                }
            }
        }

        foreach (var connection in exportResult.Connections)
        {
            if (!nodeIds.Contains(connection.SourceNodeId))
            {
                errors.Add(new ImportError
                {
                    ErrorType = "ConnectionError",
                    ConnectionId = connection.Id,
                    Message = $"连接 {connection.Id} 的源节点 '{connection.SourceNodeId}' 不存在。",
                });
            }

            if (!nodeIds.Contains(connection.TargetNodeId))
            {
                errors.Add(new ImportError
                {
                    ErrorType = "ConnectionError",
                    ConnectionId = connection.Id,
                    Message = $"连接 {connection.Id} 的目标节点 '{connection.TargetNodeId}' 不存在。",
                });
            }
        }

        return errors;
    }
}
