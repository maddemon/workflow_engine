using FlowEngine.Application.Identity;
using FlowEngine.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowEngine.Infrastructure.Tests.Identity;

public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void HashPassword_SamePasswordTwice_ProducesDifferentHashes()
    {
        const string password = "MyStr0ng!Pass";

        var hash1 = _hasher.HashPassword(password);
        var hash2 = _hasher.HashPassword(password);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsSuccess()
    {
        const string password = "correct horse battery staple";
        var hash = _hasher.HashPassword(password);

        var result = _hasher.VerifyPassword(hash, password);

        Assert.Equal(PasswordVerifyResult.Success, result);
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ReturnsFailed()
    {
        const string password = "correct horse battery staple";
        var hash = _hasher.HashPassword(password);

        var result = _hasher.VerifyPassword(hash, "wrong password");

        Assert.Equal(PasswordVerifyResult.Failed, result);
    }

    [Fact]
    public void VerifyPassword_V2Hash_ReturnsSuccessRehashNeeded()
    {
        const string password = "needs rehash";
        var legacyHasher = new Microsoft.AspNetCore.Identity.PasswordHasher<object>(
            Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2,
            }));
        var legacyHash = legacyHasher.HashPassword(null!, password);

        var result = _hasher.VerifyPassword(legacyHash, password);

        Assert.Equal(PasswordVerifyResult.SuccessRehashNeeded, result);
    }

    [Fact]
    public void VerifyPassword_TamperedHash_ReturnsFailed()
    {
        const string password = "untampered";
        var hash = _hasher.HashPassword(password);
        var bytes = Convert.FromBase64String(hash);
        bytes[5] ^= 0xFF; // 翻转版本字节之后的某字节，保持哈希格式合法但内容错误。
        var tampered = Convert.ToBase64String(bytes);

        var result = _hasher.VerifyPassword(tampered, password);

        Assert.Equal(PasswordVerifyResult.Failed, result);
    }
}
