using FlowEngine.Core.Identity;

namespace FlowEngine.Core.Tests;

public class IdentityEntitiesTests
{
    [Fact]
    public void ApiKey_Properties_RoundTrip()
    {
        var userId = Guid.NewGuid();
        var apiKey = new ApiKey
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            User = new User { Email = "test@example.com" },
            Name = "key",
            KeyHash = "hash",
            Prefix = "pre",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            RevokedAt = DateTime.UtcNow.AddDays(2)
        };

        Assert.Equal(userId, apiKey.UserId);
        Assert.Equal("key", apiKey.Name);
        Assert.Equal("hash", apiKey.KeyHash);
        Assert.Equal("pre", apiKey.Prefix);
        Assert.NotNull(apiKey.User);
        Assert.NotNull(apiKey.ExpiresAt);
        Assert.NotNull(apiKey.RevokedAt);
    }

    [Fact]
    public void User_Properties_RoundTrip()
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = "test@example.com",
            UserName = "test",
            PasswordHash = "hash",
            DisplayName = "Test User",
            IsActive = true
        };

        Assert.Equal("test@example.com", user.Email);
        Assert.Equal("test", user.UserName);
        Assert.Equal("hash", user.PasswordHash);
        Assert.Equal("Test User", user.DisplayName);
        Assert.True(user.IsActive);
    }

    [Fact]
    public void User_IsActive_DefaultsToTrue()
    {
        var user = new User();

        Assert.True(user.IsActive);
    }

    [Fact]
    public void UserRole_Properties_RoundTrip()
    {
        var userId = Guid.NewGuid();
        var role = new UserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Role = "Admin"
        };

        Assert.Equal(userId, role.UserId);
        Assert.Equal("Admin", role.Role);
    }
}
