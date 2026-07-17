using System.Text.Json;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Abstractions;
using Mapster;
using FlowEngine.Core.Authorization;
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
    WorkflowValidator validator,
    IEventBus eventBus,
    AuditEventFactory auditFactory,
    IAuthorizationGuard authGuard)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // A6：校验器通过 DI 注入，与 WorkflowService 保持一致，避免自行 new 实例。
    private readonly WorkflowValidator _validator = validator;

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
        // RBAC：导入前校验目标项目写权限。
        if (projectId.HasValue)
        {
            await authGuard.RequireAccessAsync(ResourceKind.Project, projectId.Value, Operation.Write, cancellationToken).ConfigureAwait(false);
        }

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

        var nodes = exportResult.Nodes.Select(n => n.Adapt<NodeDefinition>()).ToList();
        var connections = exportResult.Connections.Select(c => c.Adapt<Connection>()).ToList();

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

        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

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
