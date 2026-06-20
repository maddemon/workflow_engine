using FlowEngine.Application.Identity;
using FlowEngine.Core.Identity;
using Microsoft.AspNetCore.Identity;

namespace FlowEngine.Infrastructure.Identity;

/// <summary>
/// 密码哈希实现，委托给 Microsoft.AspNetCore.Identity.PasswordHasher。
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private static readonly Microsoft.AspNetCore.Identity.PasswordHasher<User> InnerHasher = new();
    private static readonly User HasherUser = new();

    /// <inheritdoc />
    public string HashPassword(string password)
    {
        return InnerHasher.HashPassword(HasherUser, password);
    }

    /// <inheritdoc />
    public bool VerifyPassword(string hashedPassword, string password)
    {
        var result = InnerHasher.VerifyHashedPassword(HasherUser, hashedPassword, password);
        return result == PasswordVerificationResult.Success;
    }
}
