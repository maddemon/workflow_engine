using FlowEngine.Application.Dtos;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流装配服务接口。
/// </summary>
public interface IWorkflowAssemblyService
{
    /// <summary>
    /// 装配 AI 草稿为完整工作流并创建草稿记录。
    /// </summary>
    Task<AssembleWorkflowResult> AssembleAsync(
        AssembleWorkflowRequest request,
        CancellationToken cancellationToken = default);
}
