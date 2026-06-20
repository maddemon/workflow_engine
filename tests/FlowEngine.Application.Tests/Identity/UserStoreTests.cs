using FlowEngine.Application.Identity;
using FlowEngine.Core.Identity;
using FlowEngine.Infrastructure.Identity;
using FlowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Tests.Identity;

/// <summary>
/// 用户仓储测试 —— 覆盖用户创建、查询、角色管理。
/// </summary>
public class UserStoreTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly UserStore _userStore;

    /// <summary>
    /// 初始化测试，创建 SQLite 内存数据库。
    /// </summary>
    public UserStoreTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new FlowEngineDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _userStore = new UserStore(_dbContext);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task CreateAsync_Returns_User_With_Id()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new User
        {
            Email = "test@example.com",
            UserName = "testuser",
            PasswordHash = "hashed_password",
            DisplayName = "Test User",
            IsActive = true,
        };

        var result = await _userStore.CreateAsync(user, ct);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("test@example.com", result.Email);
    }

    [Fact]
    public async Task GetByIdAsync_Returns_User_When_Exists()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new User
        {
            Email = "find@example.com",
            UserName = "finduser",
            PasswordHash = "hashed_password",
        };

        await _userStore.CreateAsync(user, ct);
        var result = await _userStore.GetByIdAsync(user.Id, ct);

        Assert.NotNull(result);
        Assert.Equal("find@example.com", result.Email);
    }

    [Fact]
    public async Task GetByIdAsync_Returns_Null_When_Not_Exists()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _userStore.GetByIdAsync(Guid.NewGuid(), ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByEmailAsync_Returns_User_When_Exists()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new User
        {
            Email = "lookup@example.com",
            UserName = "lookupuser",
            PasswordHash = "hashed_password",
        };

        await _userStore.CreateAsync(user, ct);
        var result = await _userStore.GetByEmailAsync("lookup@example.com", ct);

        Assert.NotNull(result);
        Assert.Equal(user.Id, result.Id);
    }

    [Fact]
    public async Task GetByEmailAsync_Returns_Null_When_Not_Exists()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _userStore.GetByEmailAsync("nonexistent@example.com", ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_Modifies_User()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new User
        {
            Email = "update@example.com",
            UserName = "updateuser",
            PasswordHash = "hashed_password",
            DisplayName = "Old Name",
        };

        await _userStore.CreateAsync(user, ct);

        user.DisplayName = "New Name";
        await _userStore.UpdateAsync(user, ct);

        var result = await _userStore.GetByIdAsync(user.Id, ct);
        Assert.NotNull(result);
        Assert.Equal("New Name", result.DisplayName);
        Assert.NotNull(result.UpdatedAt);
    }

    [Fact]
    public async Task DeleteAsync_Sets_Deleted_True()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new User
        {
            Email = "delete@example.com",
            UserName = "deleteuser",
            PasswordHash = "hashed_password",
        };

        await _userStore.CreateAsync(user, ct);
        await _userStore.DeleteAsync(user.Id, ct);

        var result = await _userStore.GetByIdAsync(user.Id, ct);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddRoleAsync_Adds_Role()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new User
        {
            Email = "role@example.com",
            UserName = "roleuser",
            PasswordHash = "hashed_password",
        };

        await _userStore.CreateAsync(user, ct);
        await _userStore.AddRoleAsync(user.Id, "admin", ct);

        var roles = await _userStore.GetRolesAsync(user.Id, ct);

        Assert.Single(roles);
        Assert.Equal("admin", roles[0].Role);
    }

    [Fact]
    public async Task AddRoleAsync_Does_Not_Duplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new User
        {
            Email = "duplicate@example.com",
            UserName = "duplicateuser",
            PasswordHash = "hashed_password",
        };

        await _userStore.CreateAsync(user, ct);
        await _userStore.AddRoleAsync(user.Id, "admin", ct);
        await _userStore.AddRoleAsync(user.Id, "admin", ct);

        var roles = await _userStore.GetRolesAsync(user.Id, ct);

        Assert.Single(roles);
    }

    [Fact]
    public async Task RemoveRoleAsync_Removes_Role()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new User
        {
            Email = "removerole@example.com",
            UserName = "removeroleuser",
            PasswordHash = "hashed_password",
        };

        await _userStore.CreateAsync(user, ct);
        await _userStore.AddRoleAsync(user.Id, "admin", ct);
        await _userStore.RemoveRoleAsync(user.Id, "admin", ct);

        var roles = await _userStore.GetRolesAsync(user.Id, ct);

        Assert.Empty(roles);
    }

    [Fact]
    public async Task GetRolesAsync_Returns_Multiple_Roles()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new User
        {
            Email = "multirole@example.com",
            UserName = "multiroleuser",
            PasswordHash = "hashed_password",
        };

        await _userStore.CreateAsync(user, ct);
        await _userStore.AddRoleAsync(user.Id, "admin", ct);
        await _userStore.AddRoleAsync(user.Id, "editor", ct);

        var roles = await _userStore.GetRolesAsync(user.Id, ct);

        Assert.Equal(2, roles.Count);
        Assert.Contains(roles, r => r.Role == "admin");
        Assert.Contains(roles, r => r.Role == "editor");
    }

    [Fact]
    public async Task AddRoleAsync_Ignores_Nonexistent_User()
    {
        var ct = TestContext.Current.CancellationToken;
        await _userStore.AddRoleAsync(Guid.NewGuid(), "admin", ct);

        var roles = await _userStore.GetRolesAsync(Guid.NewGuid(), ct);
        Assert.Empty(roles);
    }
}
