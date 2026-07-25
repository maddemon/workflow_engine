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

        var executionId = await engine.StartAsync(workflowId, workflow, inputs, cancellationToken).ConfigureAwait(false);

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
    /// 按工作流定义 ID 分页获取执行列表（服务端分页，避免一次性物化全部记录）。
    /// </summary>
    /// <param name="workflowId">工作流定义 ID。</param>
    /// <param name="projectId">可选项目过滤。</param>
    /// <param name="status">可选执行状态过滤。</param>
    /// <param name="page">页码（从 1 开始，小于 1 时归正为 1）。</param>
    /// <param name="pageSize">每页大小（1–200，越界时自动收敛）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页后的执行摘要集合。</returns>
    public async Task<PagedResult<ExecutionSummaryDto>> GetByWorkflowAsync(
        Guid workflowId,
        Guid? projectId = null,
        ExecutionStatus? status = null,
        int page = 1,
        int pageSize = 20,
        DateTime? beforeStartedAt = null,
        CancellationToken cancellationToken = default)
    {
        // RBAC：查询工作流执行列表前校验工作流读权限。
        await authGuard.RequireAccessAsync(ResourceKind.Workflow, workflowId, Operation.Read, cancellationToken);

        // 归正分页参数，避免负值或越界导致 Skip/Take 异常或过度拉取。
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var baseQuery = dbContext.ExecutionRecords
            .AsNoTracking()
            .Where(e => e.WorkflowDefinitionId == workflowId);

        if (projectId.HasValue)
        {
            baseQuery = baseQuery.Where(e => e.ProjectId == projectId.Value);
        }

        if (status.HasValue)
        {
            baseQuery = baseQuery.Where(e => e.Status == status.Value);
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        // D-12：keyset 分页（WHERE StartedAt < lastSeen ORDER BY StartedAt DESC），深翻页不退化；
        // 提供 beforeStartedAt 时走 keyset，否则回退 OFFSET 以保持 API 向后兼容。
        var pageQuery = beforeStartedAt.HasValue
            ? baseQuery
                .Where(e => e.StartedAt < beforeStartedAt.Value)
                .OrderByDescending(e => e.StartedAt)
                .Take(pageSize)
            : baseQuery
                .OrderByDescending(e => e.StartedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize);

        // D-11：仅投影摘要字段，避免物化 NodeRecords 大 JSON 列。
        var rows = await pageQuery
            .Select(e => new ExecutionSummaryProjection
            {
                Id = e.Id,
                WorkflowDefinitionId = e.WorkflowDefinitionId,
                Status = e.Status,
                StartedAt = e.StartedAt,
                CompletedAt = e.CompletedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<ExecutionSummaryDto>
        {
            Items = rows.Select(ToSummary).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <summary>
    /// 获取指定工作流当前处于待执行/执行中状态的执行（供前端实时跟踪），仅返回少量活跃记录。
    /// </summary>
    /// <param name="workflowId">工作流定义 ID。</param>
    /// <param name="projectId">可选项目过滤。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>活跃（Pending/Running）执行摘要集合。</returns>
    public async Task<IReadOnlyCollection<ExecutionSummaryDto>> GetActiveAsync(
        Guid workflowId,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        await authGuard.RequireAccessAsync(ResourceKind.Workflow, workflowId, Operation.Read, cancellationToken);

        var query = dbContext.ExecutionRecords
            .AsNoTracking()
            .Where(e => e.WorkflowDefinitionId == workflowId
                        && (e.Status == ExecutionStatus.Pending || e.Status == ExecutionStatus.Running));

        if (projectId.HasValue)
        {
            query = query.Where(e => e.ProjectId == projectId.Value);
        }

        // D-11：仅投影摘要字段，避免物化 NodeRecords 大 JSON 列。
        var rows = await query
            .OrderByDescending(e => e.StartedAt)
            .Select(e => new ExecutionSummaryProjection
            {
                Id = e.Id,
                WorkflowDefinitionId = e.WorkflowDefinitionId,
                Status = e.Status,
                StartedAt = e.StartedAt,
                CompletedAt = e.CompletedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToSummary).ToList();
    }

    /// <summary>
    /// 执行列表查询的投影载体：仅承载摘要所需字段，不包含 NodeRecords 等大 JSON 列。
    /// </summary>
    private sealed record ExecutionSummaryProjection
    {
        public required Guid Id { get; init; }

        public required Guid WorkflowDefinitionId { get; init; }

        public required ExecutionStatus Status { get; init; }

        public required DateTime StartedAt { get; init; }

        public required DateTime? CompletedAt { get; init; }
    }

    private static ExecutionSummaryDto ToSummary(ExecutionSummaryProjection row)
    {
        return new ExecutionSummaryDto
        {
            Id = row.Id,
            WorkflowDefinitionId = row.WorkflowDefinitionId,
            Status = row.Status.ToString(),
            StartedAt = row.StartedAt,
            CompletedAt = row.CompletedAt,
        };
    }
}
