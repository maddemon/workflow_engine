using FlowEngine.Application.Dtos;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流修改服务接口。
/// </summary>
public interface IWorkflowModificationService
{
    /// <summary>
    /// 对指定工作流应用一组修改操作，创建新的草稿版本。
    /// </summary>
    Task<ModifyWorkflowResult> ModifyAsync(
        Guid workflowId,
        ModifyWorkflowRequest request,
        CancellationToken cancellationToken = default);
}
