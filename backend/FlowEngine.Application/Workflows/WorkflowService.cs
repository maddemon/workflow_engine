using FlowEngine.Application.Dtos;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流应用服务，编排工作流 CRUD 与保存校验。
/// </summary>
public sealed class WorkflowService
{
    private readonly IWorkflowRepository _workflowRepository;
    private readonly WorkflowValidator _workflowValidator;

    /// <summary>
    /// 初始化工作流应用服务。
    /// </summary>
    public WorkflowService(IWorkflowRepository workflowRepository, WorkflowValidator workflowValidator)
    {
        _workflowRepository = workflowRepository ?? throw new ArgumentNullException(nameof(workflowRepository));
        _workflowValidator = workflowValidator ?? throw new ArgumentNullException(nameof(workflowValidator));
    }

    /// <summary>
    /// 创建工作流。
    /// </summary>
    public async Task<WorkflowDto> CreateAsync(CreateWorkflowDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            ProjectId = dto.ProjectId,
            Name = dto.Name,
            CreatedBy = dto.CreatedBy,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
            Nodes = dto.Nodes,
            Connections = dto.Connections
        };

        ValidateOrThrow(workflow);
        await _workflowRepository.SaveAsync(workflow, cancellationToken).ConfigureAwait(false);

        return MapToDto(workflow);
    }

    /// <summary>
    /// 按 ID 获取最新版本工作流。
    /// </summary>
    public async Task<WorkflowDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var workflow = await _workflowRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return workflow is null ? null : MapToDto(workflow);
    }

    /// <summary>
    /// 获取所有工作流摘要列表。
    /// </summary>
    public async Task<IReadOnlyCollection<WorkflowSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var workflows = await _workflowRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return workflows.Select(MapToSummary).ToList();
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

        var existing = await _workflowRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        existing.Name = dto.Name;
        existing.IsActive = dto.IsActive;
        existing.StyleSettings = dto.StyleSettings;
        existing.Nodes = dto.Nodes;
        existing.Connections = dto.Connections;
        existing.UpdatedAt = DateTime.UtcNow;

        ValidateOrThrow(existing);
        await _workflowRepository.SaveAsync(existing, cancellationToken).ConfigureAwait(false);

        return MapToDto(existing);
    }

    /// <summary>
    /// 删除工作流的所有版本。
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await _workflowRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return false;
        }

        await _workflowRepository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
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
        var workflow = await _workflowRepository.GetByVersionAsync(id, version, cancellationToken)
            .ConfigureAwait(false);
        return workflow is null ? null : MapToDto(workflow);
    }

    /// <summary>
    /// 获取工作流的所有历史版本号。
    /// </summary>
    public async Task<IReadOnlyCollection<int>> GetVersionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _workflowRepository.GetVersionsAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private void ValidateOrThrow(Workflow workflow)
    {
        var result = _workflowValidator.Validate(workflow);
        if (!result.IsValid)
        {
            throw new InvalidOperationException("工作流校验失败：" + string.Join("; ", result.Errors));
        }
    }

    private static WorkflowDto MapToDto(Workflow workflow)
    {
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
            Nodes = workflow.Nodes,
            Connections = workflow.Connections
        };
    }

    private static WorkflowSummaryDto MapToSummary(Workflow workflow)
    {
        return new WorkflowSummaryDto
        {
            Id = workflow.Id,
            Name = workflow.Name,
            Version = workflow.Version,
            IsActive = workflow.IsActive
        };
    }
}
