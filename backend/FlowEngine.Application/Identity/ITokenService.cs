namespace FlowEngine.Application.Identity;

/// <summary>
/// JWT 令牌服务接口，负责生成认证令牌。
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// 为用户生成 JWT 访问令牌。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    /// <param name="email">邮箱地址。</param>
    /// <param name="roles">角色列表。</param>
    /// <returns>JWT 令牌字符串。</returns>
    string GenerateAccessToken(Guid userId, string email, IReadOnlyList<string> roles);
}
