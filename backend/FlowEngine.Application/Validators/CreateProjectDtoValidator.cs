using FlowEngine.Application.Dtos;
using FluentValidation;

namespace FlowEngine.Application.Validators;

/// <summary>
/// 创建项目请求校验器，作为 DTO 上 DataAnnotations 的 FluentValidation 补充。
/// </summary>
public sealed class CreateProjectDtoValidator : AbstractValidator<CreateProjectDto>
{
    /// <summary>
    /// 初始化规则：名称必填且长度受限，描述长度受限。
    /// </summary>
    public CreateProjectDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("项目名称不能为空。")
            .MaximumLength(256).WithMessage("项目名称长度不能超过 256 个字符。");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("项目描述长度不能超过 2000 个字符。");
    }
}
