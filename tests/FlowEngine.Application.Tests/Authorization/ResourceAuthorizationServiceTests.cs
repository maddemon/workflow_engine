using FlowEngine.Application.Authorization;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Tests.Authorization;

public sealed class ResourceAuthorizationServiceTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly ResourceAuthorizationService _sut;

    public ResourceAuthorizationServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        var authService = new AuthorizationService();
        _sut = new ResourceAuthorizationService(_dbContext, authService);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task CanAccessWorkflowAsync_Admin_AlwaysAllowed()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Admin", ct);

        Assert.True(await _sut.CanAccessWorkflowAsync(userId, Guid.NewGuid(), Operation.Read, ct));
        Assert.True(await _sut.CanAccessWorkflowAsync(userId, Guid.NewGuid(), Operation.Write, ct));
        Assert.True(await _sut.CanAccessWorkflowAsync(userId, Guid.NewGuid(), Operation.Delete, ct));
        Assert.True(await _sut.CanAccessWorkflowAsync(userId, Guid.NewGuid(), Operation.Execute, ct));
    }

    [Fact]
    public async Task CanAccessWorkflowAsync_Editor_AllowedReadWriteExecute()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Editor", ct);

        Assert.True(await _sut.CanAccessWorkflowAsync(userId, Guid.NewGuid(), Operation.Read, ct));
        Assert.True(await _sut.CanAccessWorkflowAsync(userId, Guid.NewGuid(), Operation.Write, ct));
        Assert.True(await _sut.CanAccessWorkflowAsync(userId, Guid.NewGuid(), Operation.Execute, ct));
        Assert.False(await _sut.CanAccessWorkflowAsync(userId, Guid.NewGuid(), Operation.Delete, ct));
    }

    [Fact]
    public async Task CanAccessWorkflowAsync_Viewer_OnlyReadAllowed()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Viewer", ct);

        Assert.True(await _sut.CanAccessWorkflowAsync(userId, Guid.NewGuid(), Operation.Read, ct));
        Assert.False(await _sut.CanAccessWorkflowAsync(userId, Guid.NewGuid(), Operation.Write, ct));
        Assert.False(await _sut.CanAccessWorkflowAsync(userId, Guid.NewGuid(), Operation.Execute, ct));
        Assert.False(await _sut.CanAccessWorkflowAsync(userId, Guid.NewGuid(), Operation.Delete, ct));
    }

    [Fact]
    public async Task CanAccessCredentialAsync_Admin_AlwaysAllowed()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Admin", ct);

        Assert.True(await _sut.CanAccessCredentialAsync(userId, Guid.NewGuid(), Operation.Read, ct));
        Assert.True(await _sut.CanAccessCredentialAsync(userId, Guid.NewGuid(), Operation.Write, ct));
        Assert.True(await _sut.CanAccessCredentialAsync(userId, Guid.NewGuid(), Operation.Delete, ct));
    }

    [Fact]
    public async Task CanAccessCredentialAsync_Editor_AllowedReadWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Editor", ct);

        Assert.True(await _sut.CanAccessCredentialAsync(userId, Guid.NewGuid(), Operation.Read, ct));
        Assert.True(await _sut.CanAccessCredentialAsync(userId, Guid.NewGuid(), Operation.Write, ct));
        Assert.False(await _sut.CanAccessCredentialAsync(userId, Guid.NewGuid(), Operation.Delete, ct));
    }

    [Fact]
    public async Task CanAccessCredentialAsync_Viewer_OnlyReadAllowed()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Viewer", ct);

        Assert.True(await _sut.CanAccessCredentialAsync(userId, Guid.NewGuid(), Operation.Read, ct));
        Assert.False(await _sut.CanAccessCredentialAsync(userId, Guid.NewGuid(), Operation.Write, ct));
        Assert.False(await _sut.CanAccessCredentialAsync(userId, Guid.NewGuid(), Operation.Delete, ct));
    }

    [Fact]
    public async Task CanAccessExecutionAsync_Editor_AllowedReadExecute()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Editor", ct);

        Assert.True(await _sut.CanAccessExecutionAsync(userId, Guid.NewGuid(), Operation.Read, ct));
        Assert.True(await _sut.CanAccessExecutionAsync(userId, Guid.NewGuid(), Operation.Execute, ct));
        Assert.False(await _sut.CanAccessExecutionAsync(userId, Guid.NewGuid(), Operation.Delete, ct));
    }

    [Fact]
    public async Task CanAccessTriggerAsync_Editor_AllowedReadWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserWithRoleAsync("Editor", ct);

        Assert.True(await _sut.CanAccessTriggerAsync(userId, Guid.NewGuid(), Operation.Read, ct));
        Assert.True(await _sut.CanAccessTriggerAsync(userId, Guid.NewGuid(), Operation.Write, ct));
        Assert.False(await _sut.CanAccessTriggerAsync(userId, Guid.NewGuid(), Operation.Delete, ct));
    }

    [Fact]
    public async Task CanAccessWorkflowAsync_NoRoles_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new User
        {
            Email = "noroles@test.com",
            UserName = "noroles",
            PasswordHash = "hash",
            IsActive = true,
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(ct);

        Assert.False(await _sut.CanAccessWorkflowAsync(user.Id, Guid.NewGuid(), Operation.Read, ct));
    }

    [Fact]
    public void ShouldMaskCredentialValues_Viewer_ReturnsTrue()
    {
        Assert.True(_sut.ShouldMaskCredentialValues(["Viewer"]));
    }

    [Fact]
    public void ShouldMaskCredentialValues_Admin_ReturnsFalse()
    {
        Assert.False(_sut.ShouldMaskCredentialValues(["Admin"]));
    }

    [Fact]
    public void ShouldMaskCredentialValues_Editor_ReturnsFalse()
    {
        Assert.False(_sut.ShouldMaskCredentialValues(["Editor"]));
    }

    [Fact]
    public void ShouldMaskCredentialValues_MixedRoles_ViewerPresent_ReturnsTrue()
    {
        Assert.True(_sut.ShouldMaskCredentialValues(["Editor", "Viewer"]));
    }

    [Fact]
    public void ShouldMaskCredentialValues_EmptyRoles_ReturnsFalse()
    {
        Assert.False(_sut.ShouldMaskCredentialValues([]));
    }

    [Fact]
    public void ShouldMaskCredentialValues_CaseInsensitive()
    {
        Assert.True(_sut.ShouldMaskCredentialValues(["viewer"]));
    }

    private async Task<Guid> SeedUserWithRoleAsync(string role, CancellationToken ct)
    {
        var user = new User
        {
            Email = $"{role.ToLower()}@test.com",
            UserName = role.ToLower(),
            PasswordHash = "hash",
            IsActive = true,
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(ct);

        _dbContext.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            Role = role,
        });
        await _dbContext.SaveChangesAsync(ct);

        return user.Id;
    }
}
