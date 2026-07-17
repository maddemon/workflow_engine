using FlowEngine.Application.Dtos;
using FluentValidation;

namespace FlowEngine.Application.Validators;

/// <summary>
/// 更新工作流请求校验器，作为 DTO 上 DataAnnotations 的 FluentValidation 补充。
/// </summary>
public sealed class UpdateWorkflowDtoValidator : AbstractValidator<UpdateWorkflowDto>
{
    /// <summary>
    /// 初始化规则：工作流名称必填。
    /// </summary>
    public UpdateWorkflowDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("工作流名称不能为空。");
    }
}
