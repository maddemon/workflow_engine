using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Core;
using Mapster;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using FlowEngine.Runtime.Executor;
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
    AuditEventFactory auditFactory,
    ExecutionCancellationRegistry cancellationRegistry) : IExecutionService
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

        // 幂等：在启动真实执行前用唯一约束抢占幂等键，避免并发重复执行（至多一次）。
        // 并发时只有一个请求能成功注册 claimId，落败者不启动而复用胜者的真实执行结果。
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var claimId = Guid.NewGuid();
            var ownerId = await idempotencyService.TryGetOrRegisterAsync(
                idempotencyKey, claimId, TimeSpan.FromSeconds(3600), cancellationToken).ConfigureAwait(false);

            if (ownerId.HasValue && ownerId.Value != claimId)
            {
                // 另一请求已抢占该幂等键：不启动新执行，等待并复用其真实执行结果（非合成成功）。
                var existingRecord = await WaitForRealExecutionAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);
                if (existingRecord is not null)
                {
                    return ExecutionMapper.MapToDto(existingRecord);
                }

                // 极端情况：抢占者未能产生真实执行（启动失败）。继续以本请求兜底启动一次（至多一次尽力）。
            }
        }

        var executionId = await engine.StartAsync(workflowId, inputs, cancellationToken).ConfigureAwait(false);

        // 将幂等键从抢占用的 claimId 指向真实执行，保证后续请求返回真实结果。
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

        return ExecutionMapper.MapToDto(record);
    }

    /// <summary>
    /// 等待并复用抢占幂等键的请求所产生的真实执行记录。
    /// 抢占者启动后会将幂等键的 ExecutionId 更新为真实值；此处轮询读取当前键指向的记录，
    /// 命中即返回，避免本请求重复启动执行。
    /// </summary>
    /// <param name="idempotencyKey">幂等键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>真实 <see cref="ExecutionRecord"/>，若等待超时或抢占者未产生真实执行则返回 null。</returns>
    private async Task<ExecutionRecord?> WaitForRealExecutionAsync(
        string idempotencyKey, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var currentId = await idempotencyService.TryGetExistingAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);
            if (currentId.HasValue)
            {
                var record = await dbContext.ExecutionRecords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == currentId.Value, cancellationToken)
                    .ConfigureAwait(false);
                if (record is not null)
                {
                    return record;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }

        return null;
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
            return (ExecutionMapper.MapToDto(record), true);
        }

        // 信号给后台 worker：若是运行中执行，worker 检测到取消后走 StateMachine.Cancel() 并落库 Cancelled；
        // 若是尚未出队的 Pending 执行，则直接落库 Cancelled，避免 worker 后续覆写回 Running。
        cancellationRegistry.TryCancel(executionId);

        if (record.Status == ExecutionStatus.Pending)
        {
            // Pending 尚未被 worker 取出执行：直接落库 Cancelled（worker 取出时会跳过终态执行，不会覆写）。
            record.Status = ExecutionStatus.Cancelled;
            record.CompletedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var cancelledEvent = new WorkflowCancelledEvent(record.Id, record.WorkflowDefinitionId);
        await eventBus.PublishAsync(cancelledEvent, cancellationToken).ConfigureAwait(false);

        return (ExecutionMapper.MapToDto(record), false);
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

        return ExecutionMapper.MapToDto(record);
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

    private static ExecutionSummaryDto MapToSummary(Core.Entities.ExecutionRecord record)
    {
        return record.Adapt<ExecutionSummaryDto>();
    }
}
