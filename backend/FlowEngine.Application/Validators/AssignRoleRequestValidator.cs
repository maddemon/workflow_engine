using FlowEngine.Application.Dtos;
using FlowEngine.Core.Authorization;
using FluentValidation;

namespace FlowEngine.Application.Validators;

/// <summary>
/// 分配角色请求校验器，集中编码原 UserRoleService.AssignRoleAsync 内的角色合法性检查。
/// </summary>
public sealed class AssignRoleRequestValidator : AbstractValidator<AssignRoleRequest>
{
    /// <summary>
    /// 初始化规则：角色必须非空且可解析为 <see cref="Role"/> 枚举。
    /// </summary>
    public AssignRoleRequestValidator()
    {
        RuleFor(x => x.Role)
            .Must(BeValidRole)
            .WithMessage("无效的角色。");
    }

    private static bool BeValidRole(string? role)
    {
        return !string.IsNullOrWhiteSpace(role)
            && Enum.TryParse<Role>(role, ignoreCase: true, out _);
    }
}
