using System.Text.Json;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Executions;

/// <summary>
/// 执行应用服务，编排工作流执行与查询。
/// </summary>
public sealed class ExecutionService(
    IEngine engine,
    FlowEngineDbContext dbContext,
    IExecutionIdempotencyService idempotencyService,
    IAuthorizationGuard authGuard,
    IEventBus eventBus,
    AuditEventFactory auditFactory) : IExecutionService
{

    /// <summary>
    /// 启动工作流执行。
    /// </summary>
    public async Task<ExecutionDto?> ExecuteAsync(
        Guid workflowId,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default,
        Dictionary<string, object>? inputs = null)
    {
        var workflow = await dbContext.Workflows
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workflowId, cancellationToken)
            .ConfigureAwait(false);
        if (workflow is null)
        {
            return null;
        }

        await authGuard.RequireAccessAsync(ResourceKind.Workflow, workflowId, Operation.Execute, cancellationToken);

        // 幂等检查：如果提供了幂等键，检查是否已存在
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var tempExecutionId = Guid.NewGuid();
            var existingExecutionId = await idempotencyService.TryGetOrRegisterAsync(
                idempotencyKey, tempExecutionId, TimeSpan.FromSeconds(3600), cancellationToken).ConfigureAwait(false);
            if (existingExecutionId.HasValue)
            {
                // 返回已存在的执行记录
                var existingRecord = await dbContext.ExecutionRecords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == existingExecutionId.Value, cancellationToken)
                    .ConfigureAwait(false);
                return existingRecord is not null ? MapToDto(existingRecord) : new ExecutionDto
                {
                    Id = existingExecutionId.Value,
                    WorkflowDefinitionId = workflowId,
                    Status = "Idempotent",
                    StartedAt = DateTime.UtcNow
                };
            }
        }

        var executionId = await engine.StartAsync(workflowId, inputs, cancellationToken).ConfigureAwait(false);

        // 更新幂等记录的 ExecutionId 为实际值
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var dedupRecord = await dbContext.ExecutionDedups
                .FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            if (dedupRecord is not null && dedupRecord.ExecutionId != executionId.Value)
            {
                dedupRecord.ExecutionId = executionId.Value;
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.ExecutionStarted,
            "Execution",
            executionId.Value,
            new Dictionary<string, object> { ["workflowDefinitionId"] = workflowId }),
            cancellationToken).ConfigureAwait(false);

        var record = await dbContext.ExecutionRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == executionId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return new ExecutionDto
            {
                Id = executionId.Value,
                WorkflowDefinitionId = workflowId,
                Status = ExecutionStatus.Pending.ToString(),
                StartedAt = DateTime.UtcNow
            };
        }

        return MapToDto(record);
    }

    /// <summary>
    /// 取消执行。仅 Pending 或 Running 状态可取消；返回 null 表示执行不存在，Conflict 表示状态不可取消。
    /// </summary>
    public async Task<(ExecutionDto? Execution, bool Conflict)> CancelAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        await authGuard.RequireAccessAsync(ResourceKind.Execution, executionId, Operation.Execute, cancellationToken);

        var record = await dbContext.ExecutionRecords
            .FirstOrDefaultAsync(e => e.Id == executionId, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return (null, false);
        }

        if (record.Status is not (ExecutionStatus.Pending or ExecutionStatus.Running))
        {
            return (MapToDto(record), true);
        }

        record.Status = ExecutionStatus.Cancelled;
        record.CompletedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var cancelledEvent = new WorkflowCancelledEvent(record.Id, record.WorkflowDefinitionId);
        await eventBus.PublishAsync(cancelledEvent, cancellationToken).ConfigureAwait(false);

        return (MapToDto(record), false);
    }

    /// <summary>
    /// 按 ID 获取执行详情。
    /// </summary>
    public async Task<ExecutionDto?> GetAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        await authGuard.RequireAccessAsync(ResourceKind.Execution, executionId, Operation.Read, cancellationToken);

        var record = await dbContext.ExecutionRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == executionId, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        return MapToDto(record);
    }

    /// <summary>
    /// 按工作流定义 ID 获取执行列表。
    /// </summary>
    public async Task<IReadOnlyCollection<ExecutionSummaryDto>> GetByWorkflowAsync(
        Guid workflowId,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        // RBAC：查询工作流执行列表前校验工作流读权限。
        await authGuard.RequireAccessAsync(ResourceKind.Workflow, workflowId, Operation.Read, cancellationToken);

        var query = dbContext.ExecutionRecords
            .AsNoTracking()
            .Where(e => e.WorkflowDefinitionId == workflowId);

        if (projectId.HasValue)
        {
            query = query.Where(e => e.ProjectId == projectId.Value);
        }

        var records = await query
            .OrderByDescending(e => e.StartedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return records.Select(MapToSummary).ToList();
    }

    private static ExecutionDto MapToDto(Core.Entities.ExecutionRecord record)
    {
        return new ExecutionDto
        {
            Id = record.Id,
            WorkflowDefinitionId = record.WorkflowDefinitionId,
            Status = record.Status.ToString(),
            StartedAt = record.StartedAt,
            CompletedAt = record.CompletedAt,
            NodeRecords = record.NodeRecords.Select(MapToNodeRecord).ToList()
        };
    }

    private static NodeExecutionRecordDto MapToNodeRecord(Core.Entities.NodeExecutionRecord node)
    {
        return new NodeExecutionRecordDto
        {
            Id = node.Id,
            NodeDefinitionId = node.NodeDefinitionId,
            RunIndex = node.RunIndex,
            Status = node.Output.Success ? "Completed" : "Failed",
            StartedAt = node.StartedAt ?? default,
            CompletedAt = node.CompletedAt,
            Inputs = SerializeInputs(node.Inputs),
            Output = node.Output is null ? null : JsonSerializer.SerializeToNode(node.Output, JsonDefaults.Options),
            RawParameters = SerializeToDictionary(node.RawParameters),
            ResolvedParameters = SerializeToDictionary(node.ResolvedParameters)
        };
    }

    private static ExecutionSummaryDto MapToSummary(Core.Entities.ExecutionRecord record)
    {
        return new ExecutionSummaryDto
        {
            Id = record.Id,
            WorkflowDefinitionId = record.WorkflowDefinitionId,
            Status = record.Status.ToString(),
            StartedAt = record.StartedAt,
            CompletedAt = record.CompletedAt
        };
    }

    private static Dictionary<string, object>? SerializeInputs(IReadOnlyDictionary<string, Core.Entities.DataBatch>? inputs)
    {
        if (inputs is null || inputs.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, object>(inputs.Count);
        foreach (var (key, value) in inputs)
        {
            result[key] = JsonSerializer.SerializeToNode(value, JsonDefaults.Options) ?? string.Empty;
        }

        return result;
    }

    private static Dictionary<string, object>? SerializeToDictionary<TKey>(IReadOnlyDictionary<TKey, object>? dict)
        where TKey : notnull
    {
        if (dict is null || dict.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, object>(dict.Count);
        foreach (var (key, value) in dict)
        {
            result[key.ToString()!] = value is string or int or long or double or float or decimal or bool or DateTime
                ? value
                : JsonSerializer.SerializeToNode(value, JsonDefaults.Options) ?? string.Empty;
        }

        return result;
    }
}
