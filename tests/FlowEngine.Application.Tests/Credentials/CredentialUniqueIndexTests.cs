using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowEngine.Application.Tests.Credentials;

/// <summary>
/// D-15：验证凭据唯一约束跨库一致——(Name, ProjectId) 与 (Name) 两个过滤唯一索引
/// 使 SQLite 与 PostgreSQL 对 NULL ProjectId 的语义统一（全局凭据同名唯一）。
/// </summary>
public sealed class CredentialUniqueIndexTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;

    public CredentialUniqueIndexTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task DuplicateGlobalCredential_SameName_NullProject_Rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        _dbContext.Credentials.Add(new Credential { Name = "global", Type = "t", KeyVersion = "kv", ProjectId = null });
        await _dbContext.SaveChangesAsync(ct);

        _dbContext.Credentials.Add(new Credential { Name = "global", Type = "t", KeyVersion = "kv", ProjectId = null });
        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SameName_DifferentProject_Allowed()
    {
        var ct = TestContext.Current.CancellationToken;
        _dbContext.Credentials.Add(new Credential { Name = "shared", Type = "t", KeyVersion = "kv", ProjectId = Guid.NewGuid() });
        await _dbContext.SaveChangesAsync(ct);

        _dbContext.Credentials.Add(new Credential { Name = "shared", Type = "t", KeyVersion = "kv", ProjectId = Guid.NewGuid() });
        await _dbContext.SaveChangesAsync(ct);

        Assert.Equal(2, await _dbContext.Credentials.CountAsync(ct));
    }

    [Fact]
    public async Task SameName_SameProject_Rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        _dbContext.Credentials.Add(new Credential { Name = "scoped", Type = "t", KeyVersion = "kv", ProjectId = projectId });
        await _dbContext.SaveChangesAsync(ct);

        _dbContext.Credentials.Add(new Credential { Name = "scoped", Type = "t", KeyVersion = "kv", ProjectId = projectId });
        await Assert.ThrowsAsync<DbUpdateException>(() => _dbContext.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task GlobalAndScopedSameName_Allowed()
    {
        var ct = TestContext.Current.CancellationToken;
        _dbContext.Credentials.Add(new Credential { Name = "mixed", Type = "t", KeyVersion = "kv", ProjectId = null });
        await _dbContext.SaveChangesAsync(ct);

        _dbContext.Credentials.Add(new Credential { Name = "mixed", Type = "t", KeyVersion = "kv", ProjectId = Guid.NewGuid() });
        await _dbContext.SaveChangesAsync(ct);

        Assert.Equal(2, await _dbContext.Credentials.CountAsync(ct));
    }
}
