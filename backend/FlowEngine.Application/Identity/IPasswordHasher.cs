namespace FlowEngine.Application.Identity;

/// <summary>
/// 密码哈希接口。
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// 对密码进行哈希处理。
    /// </summary>
    /// <param name="password">明文密码。</param>
    /// <returns>哈希后的密码。</returns>
    string HashPassword(string password);

    /// <summary>
    /// 验证密码与哈希值是否匹配。
    /// </summary>
    /// <param name="hashedPassword">已存储的哈希值。</param>
    /// <param name="password">待验证的明文密码。</param>
    /// <returns>是否匹配。</returns>
    bool VerifyPassword(string hashedPassword, string password);
}
