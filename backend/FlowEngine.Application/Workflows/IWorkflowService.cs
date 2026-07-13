using FlowEngine.Application.Dtos;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流查询与草稿确认服务接口。
/// </summary>
public interface IWorkflowService
{
    /// <summary>
    /// 按 ID 获取最新版本工作流。
    /// </summary>
    Task<WorkflowDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页获取工作流摘要列表。
    /// </summary>
    Task<PagedResult<WorkflowSummaryDto>> GetAllAsync(
        Guid? projectId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 确认工作流草稿（将 IsActive 设为 true）。
    /// </summary>
    Task<WorkflowDto?> ConfirmDraftAsync(Guid id, CancellationToken cancellationToken = default);
}
