using FlowEngine.Application.Dtos;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流结构化校验服务接口。
/// </summary>
public interface IWorkflowValidationService
{
    /// <summary>
    /// 校验工作流定义。
    /// </summary>
    Task<ValidateWorkflowResult> ValidateAsync(
        ValidateWorkflowRequest request,
        CancellationToken cancellationToken = default);
}
