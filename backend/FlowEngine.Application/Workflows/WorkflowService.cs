using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Triggers;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

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
    IUserContext userContext,
    IResourceAuthorizationService resourceAuthorization)
{
    /// <summary>
    /// 创建工作流。允许 ProjectId = null 作为未分类工作流；ProjectId 仅用于分类，不做隔离校验。
    /// </summary>
    public async Task<WorkflowDto> CreateAsync(CreateWorkflowDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var (nodes, connections, nodeIdMap) = ConvertFromDtos(dto.Nodes, dto.Connections);

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

        return MapToDto(workflow, dto.Nodes, dto.Connections, nodeIdMap);
    }

    /// <summary>
    /// 按 ID 获取最新版本工作流。
    /// </summary>
    public async Task<WorkflowDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = userContext.UserId;
        if (userId is null)
        {
            throw new PermissionDeniedException("当前用户未认证。");
        }

        if (!await resourceAuthorization.CanAccessWorkflowAsync(userId.Value, id, Operation.Read, cancellationToken).ConfigureAwait(false))
        {
            await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                AuditEventTypes.PermissionDenied,
                "Workflow",
                id,
                new Dictionary<string, object> { ["operation"] = Operation.Read.ToString(), ["reason"] = "role" }),
                cancellationToken).ConfigureAwait(false);

            throw new PermissionDeniedException("当前用户没有读取该工作流的权限。");
        }

        var workflow = await dbContext.Workflows
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (workflow is null)
        {
            return null;
        }

        return MapToDto(workflow);
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

        var query = dbContext.Workflows.AsQueryable();
        if (projectId.HasValue)
        {
            query = query.Where(w => w.ProjectId == projectId.Value);
        }

        var totalCount = await query
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var workflows = await query
            .OrderBy(w => w.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // BE-01: 批量查询关联数据，避免每行 N+1。
        var workflowIds = workflows.Select(w => w.Id).ToList();

        var lastExecutions = await dbContext.ExecutionRecords
            .Where(e => workflowIds.Contains(e.WorkflowDefinitionId) && e.CompletedAt != null)
            .GroupBy(e => e.WorkflowDefinitionId)
            .Select(g => new { WorkflowId = g.Key, LastCompletedAt = g.Max(e => e.CompletedAt) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var triggerStats = await dbContext.Triggers
            .Where(t => workflowIds.Contains(t.WorkflowDefinitionId) && !t.Deleted)
            .GroupBy(t => t.WorkflowDefinitionId)
            .Select(g => new { WorkflowId = g.Key, Count = g.Count(), NextTriggerAt = g.Min(t => t.NextTriggerAt) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lastExecMap = lastExecutions.ToDictionary(x => x.WorkflowId, x => x.LastCompletedAt);
        var triggerMap = triggerStats.ToDictionary(x => x.WorkflowId);

        var items = workflows.Select(w =>
        {
            var lastExec = lastExecMap.GetValueOrDefault(w.Id);
            var triggerStat = triggerMap.GetValueOrDefault(w.Id);
            return new WorkflowSummaryDto
            {
                Id = w.Id,
                Name = w.Name,
                Version = w.Version,
                IsActive = w.IsActive,
                ProjectId = w.ProjectId,
                CreatedAt = w.CreatedAt,
                UpdatedAt = w.UpdatedAt,
                LastExecutionAt = lastExec,
                TriggerCount = triggerStat?.Count ?? 0,
                NextTriggerAt = triggerStat?.NextTriggerAt,
            };
        }).ToList();

        return new PagedResult<WorkflowSummaryDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
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

        var userId = userContext.UserId;
        if (userId is null)
        {
            throw new PermissionDeniedException("当前用户未认证。");
        }

        if (!await resourceAuthorization.CanAccessWorkflowAsync(userId.Value, id, Operation.Write, cancellationToken).ConfigureAwait(false))
        {
            await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                AuditEventTypes.PermissionDenied,
                "Workflow",
                id,
                new Dictionary<string, object> { ["operation"] = Operation.Write.ToString(), ["reason"] = "role" }),
                cancellationToken).ConfigureAwait(false);

            throw new PermissionDeniedException("当前用户没有修改该工作流的权限。");
        }

        var existing = await dbContext.Workflows
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        if (!CanWriteWorkflow())
        {
            throw new PermissionDeniedException("当前用户没有修改工作流的权限。");
        }

        var previousIsActive = existing.IsActive;
        var (nodes, connections, nodeIdMap) = ConvertFromDtos(dto.Nodes, dto.Connections);

        existing.Name = dto.Name;
        existing.IsActive = dto.IsActive;
        existing.StyleSettings = dto.StyleSettings;
        existing.Nodes = nodes;
        existing.Connections = connections;
        existing.UpdatedAt = DateTime.UtcNow;

        ValidateOrThrow(existing);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (previousIsActive && !existing.IsActive)
        {
            await UnregisterTriggersAsync(existing.Id, cancellationToken).ConfigureAwait(false);
            await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                AuditEventTypes.WorkflowDeactivated,
                "Workflow",
                existing.Id,
                new Dictionary<string, object> { ["name"] = existing.Name }),
                cancellationToken).ConfigureAwait(false);
        }
        else if (!previousIsActive && existing.IsActive)
        {
            await RegisterTriggersAsync(existing.Id, cancellationToken).ConfigureAwait(false);
            await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                AuditEventTypes.WorkflowActivated,
                "Workflow",
                existing.Id,
                new Dictionary<string, object> { ["name"] = existing.Name }),
                cancellationToken).ConfigureAwait(false);
        }

        await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.WorkflowUpdated,
            "Workflow",
            existing.Id,
            new Dictionary<string, object> { ["name"] = existing.Name }),
            cancellationToken).ConfigureAwait(false);

        return MapToDto(existing, dto.Nodes, dto.Connections, nodeIdMap);
    }

    /// <summary>
    /// 删除工作流的所有版本。
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = userContext.UserId;
        if (userId is null)
        {
            throw new PermissionDeniedException("当前用户未认证。");
        }

        if (!await resourceAuthorization.CanAccessWorkflowAsync(userId.Value, id, Operation.Delete, cancellationToken).ConfigureAwait(false))
        {
            await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                AuditEventTypes.PermissionDenied,
                "Workflow",
                id,
                new Dictionary<string, object> { ["operation"] = Operation.Delete.ToString(), ["reason"] = "role" }),
                cancellationToken).ConfigureAwait(false);

            throw new PermissionDeniedException("当前用户没有删除该工作流的权限。");
        }

        var existing = await dbContext.Workflows
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return false;
        }

        if (!IsSystemAdmin())
        {
            throw new PermissionDeniedException("仅管理员可删除工作流。");
        }

        dbContext.Workflows.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await UnregisterTriggersAsync(id, cancellationToken).ConfigureAwait(false);
        await _triggerService.DeleteByWorkflowDefinitionIdAsync(id, cancellationToken).ConfigureAwait(false);

        await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.WorkflowDeleted,
            "Workflow",
            id),
            cancellationToken).ConfigureAwait(false);

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
        var workflow = await dbContext.Workflows
            .FirstOrDefaultAsync(w => w.Id == id && w.Version == version, cancellationToken)
            .ConfigureAwait(false);
        return workflow is null ? null : MapToDto(workflow);
    }

    /// <summary>
    /// 获取工作流的所有历史版本号。
    /// </summary>
    public async Task<IReadOnlyCollection<int>> GetVersionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Workflows
            .Where(w => w.Id == id)
            .Select(w => w.Version)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 将 API DTO 转换为领域实体，生成新的 Guid ID 并建立前端字符串 ID 到 Guid 的映射。
    /// </summary>
    private static (List<NodeDefinition> Nodes, List<Connection> Connections, Dictionary<string, Guid> NodeIdMap) ConvertFromDtos(
        List<NodeDefinitionDto> nodeDtos,
        List<ConnectionDto> connectionDtos)
    {
        var nodeIdMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var nodes = nodeDtos.Select(dto => WorkflowMapper.ToEntity(dto, nodeIdMap)).ToList();
        var connections = connectionDtos.Select(dto => WorkflowMapper.ToEntity(dto, nodeIdMap)).ToList();
        return (nodes, connections, nodeIdMap);
    }

    private void ValidateOrThrow(Workflow workflow)
    {
        var result = _workflowValidator.Validate(workflow);
        if (!result.IsValid)
        {
            throw new BusinessException("工作流校验失败：" + string.Join("; ", result.Errors));
        }
    }

    private bool CanWriteWorkflow()
    {
        return userContext.Roles.Contains(RoleConstants.Admin) || userContext.Roles.Contains(RoleConstants.Editor);
    }

    /// <summary>
    /// 判断当前用户是否为系统 Admin（全局角色）。
    /// </summary>
    private bool IsSystemAdmin()
    {
        return userContext.Roles.Contains(RoleConstants.Admin);
    }

    private static NodeDefinitionDto BuildNodeDto(NodeDefinition n, string id)
    {
        return new NodeDefinitionDto
        {
            Id = id,
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
    }

    private static ConnectionDto BuildConnectionDto(Connection c, string id, string sourceNodeId, string targetNodeId)
    {
        return new ConnectionDto
        {
            Id = id,
            SourceNodeId = sourceNodeId,
            SourcePortName = c.SourcePortName,
            TargetNodeId = targetNodeId,
            TargetPortName = c.TargetPortName,
            Condition = c.Condition,
        };
    }

    /// <summary>
    /// 将领域实体转换为 API 响应 DTO（从数据库加载时使用）。
    /// </summary>
    private static WorkflowDto MapToDto(Workflow workflow)
    {
        var nodeDtos = workflow.Nodes.Select(n => WorkflowMapper.ToDto(n, n.Id.ToString())).ToList();
        var connectionDtos = workflow.Connections.Select(c =>
            WorkflowMapper.ToDto(c, c.Id.ToString(), c.SourceNodeId.ToString(), c.TargetNodeId.ToString())).ToList();

        return new WorkflowDto
        {
            Id = workflow.Id,
            ProjectId = workflow.ProjectId,
            Name = workflow.Name,
            Version = workflow.Version,
            CreatedBy = workflow.CreatedBy,
            CreatedAt = workflow.CreatedAt,
            UpdatedAt = workflow.UpdatedAt,
            IsActive = workflow.IsActive,
            StyleSettings = workflow.StyleSettings,
            Nodes = nodeDtos,
            Connections = connectionDtos,
        };
    }

    /// <summary>
    /// 将领域实体转换为 API 响应 DTO（保存后返回时使用，保持前端原始 ID）。
    /// </summary>
    private static WorkflowDto MapToDto(
        Workflow workflow,
        List<NodeDefinitionDto> originalNodeDtos,
        List<ConnectionDto> originalConnectionDtos,
        Dictionary<string, Guid> nodeIdMap)
    {
        var reverseNodeIdMap = nodeIdMap.ToDictionary(kv => kv.Value, kv => kv.Key);

        var nodeDtos = workflow.Nodes.Select(n =>
        {
            var originalId = reverseNodeIdMap.TryGetValue(n.Id, out var origId) ? origId : n.Id.ToString();
            return WorkflowMapper.ToDto(n, originalId);
        }).ToList();

        var connectionDtos = workflow.Connections.Select(c =>
        {
            var origSource = reverseNodeIdMap.TryGetValue(c.SourceNodeId, out var sId) ? sId : c.SourceNodeId.ToString();
            var origTarget = reverseNodeIdMap.TryGetValue(c.TargetNodeId, out var tId) ? tId : c.TargetNodeId.ToString();
            var origConn = originalConnectionDtos.FirstOrDefault(cd =>
                cd.SourceNodeId == origSource && cd.TargetNodeId == origTarget);

            return WorkflowMapper.ToDto(c, origConn?.Id ?? c.Id.ToString(), origSource, origTarget);
        }).ToList();

        return new WorkflowDto
        {
            Id = workflow.Id,
            ProjectId = workflow.ProjectId,
            Name = workflow.Name,
            Version = workflow.Version,
            CreatedBy = workflow.CreatedBy,
            CreatedAt = workflow.CreatedAt,
            UpdatedAt = workflow.UpdatedAt,
            IsActive = workflow.IsActive,
            StyleSettings = workflow.StyleSettings,
            Nodes = nodeDtos,
            Connections = connectionDtos,
        };
    }

    private async Task RegisterTriggersAsync(Guid workflowDefinitionId, CancellationToken cancellationToken)
    {
        await _triggerService.RegisterWorkflowSchedulesAsync(workflowDefinitionId, cancellationToken).ConfigureAwait(false);
    }

    private async Task UnregisterTriggersAsync(Guid workflowDefinitionId, CancellationToken cancellationToken)
    {
        await _triggerService.UnregisterWorkflowSchedulesAsync(workflowDefinitionId, cancellationToken).ConfigureAwait(false);
    }
}
