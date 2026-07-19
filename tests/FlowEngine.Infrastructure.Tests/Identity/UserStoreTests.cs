using FlowEngine.Application.Identity;
using FlowEngine.Core.Data;
using FlowEngine.Core.Identity;
using FlowEngine.Infrastructure.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowEngine.Infrastructure.Tests.Identity;

public sealed class UserStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FlowEngineDbContext _dbContext;
    private readonly UserStore _store;

    public UserStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FlowEngineDbContext(options);
        _dbContext.Database.EnsureCreated();
        _store = new UserStore(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateAsync_AddsUser()
    {
        var user = new User
        {
            Email = "create@example.com",
            UserName = "create",
            PasswordHash = "hash",
        };

        var created = await _store.CreateAsync(user, Ct);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.True(created.CreatedAt > DateTime.MinValue);
        Assert.Equal("create@example.com", (await _store.GetByEmailAsync("create@example.com", Ct))?.Email);
    }

    [Fact]
    public async Task GetByIdAsync_Existing_ReturnsUser()
    {
        var user = await CreateUserAsync("byid@example.com", "byid");

        var found = await _store.GetByIdAsync(user.Id, Ct);

        Assert.NotNull(found);
        Assert.Equal(user.Email, found.Email);
    }

    [Fact]
    public async Task GetByIdAsync_Deleted_ReturnsNull()
    {
        var user = await CreateUserAsync("deleted-id@example.com", "deleted-id");
        user.Deleted = true;
        await _dbContext.SaveChangesAsync(Ct);

        var found = await _store.GetByIdAsync(user.Id, Ct);

        Assert.Null(found);
    }

    [Fact]
    public async Task GetByEmailAsync_Missing_ReturnsNull()
    {
        var found = await _store.GetByEmailAsync("missing@example.com", Ct);

        Assert.Null(found);
    }

    [Fact]
    public async Task UpdateAsync_SetsUpdatedAt()
    {
        var user = await CreateUserAsync("update@example.com", "update");
        Assert.Null(user.UpdatedAt);

        user.DisplayName = "Updated";
        await _store.UpdateAsync(user, Ct);

        var updated = await _store.GetByIdAsync(user.Id, Ct);
        Assert.NotNull(updated);
        Assert.NotNull(updated.UpdatedAt);
        Assert.True(updated.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesUser()
    {
        var user = await CreateUserAsync("delete@example.com", "delete");

        await _store.DeleteAsync(user.Id, Ct);

        Assert.Null(await _store.GetByIdAsync(user.Id, Ct));
        Assert.True(_dbContext.Users.AsNoTracking().Single(u => u.Id == user.Id).Deleted);
    }

    [Fact]
    public async Task DeleteAsync_MissingUser_DoesNotThrow()
    {
        await _store.DeleteAsync(Guid.NewGuid(), Ct);
    }

    [Fact]
    public async Task AddRoleAsync_AddsRole()
    {
        var user = await CreateUserAsync("role@example.com", "role");

        await _store.AddRoleAsync(user.Id, "Admin", Ct);
        var roles = await _store.GetRolesAsync(user.Id, Ct);

        Assert.Single(roles);
        Assert.Equal("Admin", roles[0].Role);
    }

    [Fact]
    public async Task AddRoleAsync_Duplicate_IsIgnored()
    {
        var user = await CreateUserAsync("role-dup@example.com", "role-dup");
        await _store.AddRoleAsync(user.Id, "Admin", Ct);

        await _store.AddRoleAsync(user.Id, "Admin", Ct);

        var roles = await _store.GetRolesAsync(user.Id, Ct);
        Assert.Single(roles);
    }

    [Fact]
    public async Task GetRolesAsync_ExcludesDeleted()
    {
        var user = await CreateUserAsync("role-deleted@example.com", "role-deleted");
        var role = new UserRole { UserId = user.Id, Role = "Admin" };
        _dbContext.UserRoles.Add(role);
        await _dbContext.SaveChangesAsync(Ct);
        role.Deleted = true;
        await _dbContext.SaveChangesAsync(Ct);

        var roles = await _store.GetRolesAsync(user.Id, Ct);

        Assert.Empty(roles);
    }

    [Fact]
    public async Task RemoveRoleAsync_SoftDeletesRole()
    {
        var user = await CreateUserAsync("role-remove@example.com", "role-remove");
        await _store.AddRoleAsync(user.Id, "Admin", Ct);

        await _store.RemoveRoleAsync(user.Id, "Admin", Ct);

        Assert.Empty(await _store.GetRolesAsync(user.Id, Ct));
    }

    [Fact]
    public async Task RemoveRoleAsync_MissingRole_DoesNotThrow()
    {
        var user = await CreateUserAsync("role-missing@example.com", "role-missing");

        await _store.RemoveRoleAsync(user.Id, "Admin", Ct);
    }

    private async Task<User> CreateUserAsync(string email, string userName)
    {
        var user = new User
        {
            Email = email,
            UserName = userName,
            PasswordHash = "hash",
        };
        return await _store.CreateAsync(user, Ct);
    }
}
