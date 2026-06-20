namespace FlowEngine.Application.Identity;

/// <summary>
/// 当前用户上下文接口，提供请求级别的当前用户信息。
/// </summary>
public interface IUserContext
{
    /// <summary>
    /// 当前用户是否已认证。
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// 当前用户 ID（未认证时为 null）。
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// 当前用户邮箱（未认证时为 null）。
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// 当前用户角色列表。
    /// </summary>
    IReadOnlyList<string> Roles { get; }
}
