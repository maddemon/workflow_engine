using FlowEngine.Application.Dtos;
using FluentValidation;

namespace FlowEngine.Application.Validators;

/// <summary>
/// 创建凭据请求校验器，作为 DTO 上 DataAnnotations 的 FluentValidation 补充。
/// </summary>
public sealed class CreateCredentialDtoValidator : AbstractValidator<CreateCredentialDto>
{
    /// <summary>
    /// 初始化规则：名称、类型必填且长度受限，字段映射不可为空。
    /// </summary>
    public CreateCredentialDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("凭据名称不能为空。")
            .MaximumLength(256).WithMessage("凭据名称长度不能超过 256 个字符。");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("凭据类型不能为空。")
            .MaximumLength(128).WithMessage("凭据类型长度不能超过 128 个字符。");

        RuleFor(x => x.Fields)
            .NotNull().WithMessage("凭据字段映射不能为空。")
            .Must(fields => fields.Count > 0).WithMessage("凭据字段映射不能为空。");
    }
}
