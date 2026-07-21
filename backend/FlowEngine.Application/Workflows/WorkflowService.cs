using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Triggers;
using Mapster;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流应用服务，编排工作流 CRUD 与保存校验。
/// </summary>
public sealed class WorkflowService(
    FlowEngineDbContext dbContext,
    WorkflowValidator _workflowValidator,
    IEventBus eventBus,
    AuditEventFactory auditFactory,
    TriggerService _triggerService,
    IAuthorizationGuard authGuard,
    AuthorizedOperationHandler handler,
    WorkflowStatisticsLoader statisticsLoader,
    WorkflowTriggerSync triggerSync,
    ILogger<WorkflowService> logger) : IWorkflowService
{
    private static readonly AuthorizationPolicy UpdatePolicy = new(
        ResourceKind.Workflow, Operation.Write, Scope.Workflow, AdminPhase: false, ProjectScoped: false);
    private static readonly AuthorizationPolicy DeletePolicy = new(
        ResourceKind.Workflow, Operation.Delete, Scope: null, AdminPhase: true, ProjectScoped: false);

    /// <summary>
    /// 创建工作流。允许 ProjectId = null 作为未分类工作流；ProjectId 仅用于分类，不做隔离校验。
    /// </summary>
    public async Task<WorkflowDto> CreateAsync(CreateWorkflowDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var (nodes, connections) = ConvertFromDtos(dto.Nodes, dto.Connections);

        var workflow = new Workflow
        {
            ProjectId = dto.ProjectId,
            Name = dto.Name,
            CreatedBy = dto.CreatedBy,
            IsActive = true,
            Nodes = nodes,
            Connections = connections
        };

        ValidateOrThrow(workflow);
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.WorkflowCreated,
            "Workflow",
            workflow.Id,
            new Dictionary<string, object> { ["name"] = workflow.Name }),
            cancellationToken).ConfigureAwait(false);

        return workflow.Adapt<WorkflowDto>();
    }

    /// <summary>
    /// 按 ID 获取最新版本工作流。
    /// </summary>
    public async Task<WorkflowDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await authGuard.RequireAccessAsync(ResourceKind.Workflow, id, Operation.Read, cancellationToken);

        var workflow = await dbContext.Workflows
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (workflow is null)
        {
            return null;
        }

        return workflow.Adapt<WorkflowDto>();
    }

    /// <summary>
    /// 分页获取工作流摘要列表。项目（ProjectId）仅作为分类字段，不对可见性做隔离。
    /// </summary>
    /// <param name="page">页码（从 1 开始）。</param>
    /// <param name="pageSize">每页大小。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<PagedResult<WorkflowSummaryDto>> GetAllAsync(
        Guid? projectId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = projectId.HasValue
            ? dbContext.Workflows.AsNoTracking().Where(w => w.ProjectId == projectId.Value)
            : dbContext.Workflows.AsNoTracking().AsQueryable();

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var workflows = await query
            .OrderBy(w => w.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // BE-01: 批量查询关联数据，避免每行 N+1（统计逻辑下沉至 WorkflowStatisticsLoader）。
        var stats = await statisticsLoader.LoadAsync(
            workflows.Select(w => w.Id).ToList(), cancellationToken).ConfigureAwait(false);

        var items = workflows.ConvertAll(w =>
        {
            var stat = stats.GetValueOrDefault(w.Id);
            return w.Adapt<WorkflowSummaryDto>() with
            {
                LastExecutionAt = stat?.LastExecutionAt,
                TriggerCount = stat?.TriggerCount ?? 0,
                NextTriggerAt = stat?.NextTriggerAt,
            };
        });

        return new PagedResult<WorkflowSummaryDto>
        { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize };
    }

    /// <summary>
    /// 更新工作流并递增版本号。
    /// </summary>
    public async Task<WorkflowDto?> UpdateAsync(
        Guid id,
        UpdateWorkflowDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        await handler.AuthorizePreAsync(UpdatePolicy, id, cancellationToken);

        var existing = await dbContext.Workflows
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        var previousIsActive = existing.IsActive;
        var (nodes, connections) = ConvertFromDtos(dto.Nodes, dto.Connections);

        // 内容真正变更时才递增版本号，避免无意义的版本膨胀（修复：UpdateAsync 从不递增 Version）。
        var contentChanged = HasContentChanged(existing, dto, nodes, connections);

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // 先注销：如果从激活变为停用，先注销触发器调度。
            if (previousIsActive && !dto.IsActive)
            {
                await triggerSync.UnregisterTriggersAsync(id, cancellationToken).ConfigureAwait(false);
            }

            existing.Name = dto.Name;
            existing.IsActive = dto.IsActive;
            existing.StyleSettings = dto.StyleSettings;
            existing.Nodes = nodes;
            existing.Connections = connections;
            existing.UpdatedAt = DateTime.UtcNow;

            if (contentChanged)
            {
                existing.Version += 1;
            }

            ValidateOrThrow(existing);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // 注册新调度：如果从停用变为激活，尝试注册触发器。
            if (!previousIsActive && dto.IsActive)
            {
                try
                {
                    await triggerSync.RegisterTriggersAsync(id, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 补偿日志：注册新调度失败，记录但不回滚数据库事务。
                    logger.LogError(ex,
                        "工作流 {WorkflowId} 激活后注册触发器调度失败，数据库已保存但调度未恢复，需人工补偿。",
                        id);
                }
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        await handler.PublishAuditAsync(
            AuditEventTypes.WorkflowUpdated,
            "Workflow",
            existing.Id,
            new Dictionary<string, object> { ["name"] = existing.Name },
            cancellationToken).ConfigureAwait(false);

        return existing.Adapt<WorkflowDto>();
    }

    /// <summary>
    /// 创建工作流草稿（IsActive = false）。
    /// </summary>
    public async Task<WorkflowDto> CreateDraftAsync(CreateWorkflowDto dto, CancellationToken cancellationToken = default, WorkflowSource source = WorkflowSource.Human)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var (nodes, connections) = ConvertFromDtos(dto.Nodes, dto.Connections);

        var workflow = new Workflow
        {
            ProjectId = dto.ProjectId,
            Name = dto.Name,
            CreatedBy = dto.CreatedBy,
            IsActive = false,
            Nodes = nodes,
            Connections = connections
        };

        workflow.Source = source;

        ValidateOrThrow(workflow);
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.WorkflowCreated,
            "Workflow",
            workflow.Id,
            new Dictionary<string, object> { ["name"] = workflow.Name }),
            cancellationToken).ConfigureAwait(false);

        return workflow.Adapt<WorkflowDto>();
    }

    /// <summary>
    /// 确认工作流草稿（将 IsActive 设为 true）。
    /// </summary>
    public async Task<WorkflowDto?> ConfirmDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // RBAC：确认草稿前校验工作流写权限。
        await authGuard.RequireAccessAsync(ResourceKind.Workflow, id, Operation.Write, cancellationToken);

        var existing = await dbContext.Workflows
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        existing.IsActive = true;
        existing.DraftStatus = DraftStatus.Confirmed;
        existing.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return existing.Adapt<WorkflowDto>();
    }

    /// <summary>
    /// 拒绝工作流草稿（设置拒绝理由，将 DraftStatus 设为 Rejected）。
    /// </summary>
    public async Task<WorkflowDto?> RejectDraftAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        // RBAC：拒绝草稿前校验工作流写权限。
        await authGuard.RequireAccessAsync(ResourceKind.Workflow, id, Operation.Write, cancellationToken);

        var existing = await dbContext.Workflows
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        existing.RejectionReason = reason;
        existing.DraftStatus = DraftStatus.Rejected;
        existing.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return existing.Adapt<WorkflowDto>();
    }

    /// <summary>
    /// 删除工作流的所有版本。
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await handler.AuthorizePreAsync(DeletePolicy, id, cancellationToken);

        var existing = await dbContext.Workflows
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return false;
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // 先注销触发器调度（Quartz 外部状态，不在 DB 事务内）。
            try
            {
                await triggerSync.UnregisterTriggersAsync(id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 补偿日志：注销调度失败，继续删除数据库记录，但调度可能残留。
                logger.LogError(ex,
                    "工作流 {WorkflowId} 删除前注销触发器调度失败，数据库将删除但调度可能残留，需人工清理。",
                    id);
            }

            dbContext.Workflows.Remove(existing);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await _triggerService.DeleteByWorkflowDefinitionIdAsync(id, cancellationToken).ConfigureAwait(false);

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        await handler.PublishAuditAsync(
            AuditEventTypes.WorkflowDeleted,
            "Workflow",
            id,
            ct: cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// 按版本号获取工作流。
    /// </summary>
    public async Task<WorkflowDto?> GetVersionAsync(
        Guid id,
        int version,
        CancellationToken cancellationToken = default)
    {
        // M6：历史版本接口同样需要资源归属校验，防止越权读取。
        await authGuard.RequireAccessAsync(ResourceKind.Workflow, id, Operation.Read, cancellationToken);

        var workflow = await dbContext.Workflows
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id && w.Version == version, cancellationToken)
            .ConfigureAwait(false);
        return workflow is null ? null : workflow.Adapt<WorkflowDto>();
    }

    /// <summary>
    /// 获取工作流的所有历史版本号。
    /// </summary>
    public async Task<IReadOnlyCollection<int>> GetVersionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // M6：历史版本列表接口同样需要资源归属校验。
        await authGuard.RequireAccessAsync(ResourceKind.Workflow, id, Operation.Read, cancellationToken);

        return await dbContext.Workflows
            .AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => w.Version)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 将 API DTO 转换为领域实体。
    /// </summary>
    private static (List<NodeDefinition> Nodes, List<Connection> Connections) ConvertFromDtos(
        List<NodeDefinitionDto> nodeDtos,
        List<ConnectionDto> connectionDtos)
    {
        var nodes = nodeDtos.Select(n => n.Adapt<NodeDefinition>()).ToList();
        var connections = connectionDtos.Select(c => c.Adapt<Connection>()).ToList();
        return (nodes, connections);
    }

    private void ValidateOrThrow(Workflow workflow)
    {
        var result = _workflowValidator.Validate(workflow);
        if (!result.IsValid)
        {
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException("Workflow validation failed: " + string.Join("; ", result.Errors));
        }
    }

    /// <summary>
    /// 判断更新后的内容是否与现有工作流存在实质差异，从而决定是否需要递增版本号。
    /// 比较名称、激活状态、样式设置与节点/连接集合（序列化为 JSON 做结构性比较）。
    /// </summary>
    private static bool HasContentChanged(
        Workflow existing,
        UpdateWorkflowDto dto,
        List<NodeDefinition> nodes,
        List<Connection> connections)
    {
        if (!string.Equals(existing.Name, dto.Name, StringComparison.Ordinal))
        {
            return true;
        }

        if (existing.IsActive != dto.IsActive)
        {
            return true;
        }

        if (!JsonEquals(existing.StyleSettings, dto.StyleSettings))
        {
            return true;
        }

        if (!JsonEquals(existing.Nodes, nodes))
        {
            return true;
        }

        if (!JsonEquals(existing.Connections, connections))
        {
            return true;
        }

        return false;
    }

    private static bool JsonEquals<T>(T left, T right)
    {
        var leftJson = JsonSerializer.Serialize(left, JsonDefaults.Options);
        var rightJson = JsonSerializer.Serialize(right, JsonDefaults.Options);
        return leftJson == rightJson;
    }
}
