using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Projects;

/// <summary>
/// 项目应用服务，编排项目 CRUD 与成员管理。
/// </summary>
public sealed class ProjectService(
    FlowEngineDbContext dbContext,
    IUserContext userContext,
    IAuthorizationGuard authGuard,
    IEventBus eventBus,
    AuditEventFactory auditFactory,
    AuthorizedOperationHandler handler,
    ProjectCascadeDeleter cascadeDeleter)
{
    private static readonly AuthorizationPolicy UpdatePolicy = new(
        Resource: null, Access: Operation.Write, Scope: null, AdminPhase: false, ProjectScoped: true);
    private static readonly AuthorizationPolicy DeletePolicy = new(
        Resource: null, Access: Operation.Delete, Scope: null, AdminPhase: true, ProjectScoped: false);
    /// <summary>
    /// 创建项目。项目仅用于分类，不再维护成员关系。
    /// </summary>
    public async Task<ProjectDto> CreateAsync(CreateProjectDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = userContext.UserId
            ?? throw new UnauthorizedException("用户未认证。");

        var project = new Project
        {
            Name = dto.Name,
            Description = dto.Description,
            CreatedBy = userId,
        };

        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.ProjectCreated,
            "Project",
            project.Id,
            new Dictionary<string, object> { ["name"] = project.Name }),
            cancellationToken).ConfigureAwait(false);

        return MapToDto(project);
    }

    /// <summary>
    /// 获取当前用户可访问的所有项目。管理员可查看全部项目，其他用户仅可查看自己创建的项目。
    /// </summary>
    public async Task<IReadOnlyList<ProjectDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var userId = userContext.UserId
            ?? throw new UnauthorizedException("用户未认证。");

        var query = dbContext.Projects.Where(p => !p.Deleted);

        if (!IsSystemAdmin())
        {
            query = query.Where(p => p.CreatedBy == userId);
        }

        var projects = await query
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return projects.Select(MapToDto).ToList();
    }

    /// <summary>
    /// 按 ID 获取项目。
    /// </summary>
    public async Task<ProjectDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == id && !p.Deleted, cancellationToken)
            .ConfigureAwait(false);

        if (project is null)
        {
            return null;
        }

        await EnsureCanAccessProjectAsync(id, Operation.Read, cancellationToken).ConfigureAwait(false);

        return MapToDto(project);
    }

    /// <summary>
    /// 更新项目。
    /// </summary>
    public async Task<ProjectDto?> UpdateAsync(Guid id, UpdateProjectDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var project = await dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == id && !p.Deleted, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }

        await handler.AuthorizeProjectAccessAsync(id, Operation.Write, cancellationToken).ConfigureAwait(false);

        project.Name = dto.Name;
        project.Description = dto.Description;
        project.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await handler.PublishAuditAsync(
            AuditEventTypes.ProjectUpdated,
            "Project",
            project.Id,
            new Dictionary<string, object> { ["name"] = project.Name },
            cancellationToken).ConfigureAwait(false);

        return MapToDto(project);
    }

    /// <summary>
    /// 删除项目（仅系统管理员可操作）。项目仅用于分类，删除时不校验项目成员。
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await handler.AuthorizePreAsync(DeletePolicy, id, cancellationToken);

        var project = await dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == id && !p.Deleted, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            return false;
        }

        project.Deleted = true;
        project.UpdatedAt = DateTime.UtcNow;

        await cascadeDeleter.CascadeSoftDeleteAsync(id, project.UpdatedAt.Value, cancellationToken)
            .ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await handler.PublishAuditAsync(
            AuditEventTypes.ProjectDeleted,
            "Project",
            project.Id,
            ct: cancellationToken).ConfigureAwait(false);

        return true;
    }


    private async Task EnsureCanAccessProjectAsync(Guid projectId, Operation operation, CancellationToken cancellationToken)
    {
        await authGuard.RequireAccessAsync(ResourceKind.Project, projectId, operation, cancellationToken).ConfigureAwait(false);
    }

    private bool IsSystemAdmin()
    {
        return userContext.Roles.Contains(RoleConstants.Admin, StringComparer.OrdinalIgnoreCase);
    }

    private static ProjectDto MapToDto(Project project)
    {
        return new ProjectDto
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            CreatedBy = project.CreatedBy,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt,
        };
    }

}
