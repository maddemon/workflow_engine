using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace FlowEngine.Application.Tests.Data;

/// <summary>
/// Task 8：乐观并发令牌回归测试。
/// 高竞争实体（<see cref="Workflow"/>/<see cref="Project"/>/<see cref="Credential"/>）必须在并发更新时
/// 由后写者抛出 <see cref="DbUpdateConcurrencyException"/>，防止「丢失更新」（lost update）。
/// 并发令牌由应用层在 <see cref="FlowEngineDbContext.SaveChanges"/> 前自增，保证 SQLite 与
/// SQL Server/PostgreSQL/MySQL 多提供程序行为一致。
/// 注意：EF Core 的 InMemory 提供程序不强制并发令牌，因此本测试使用文件型 SQLite
/// （多连接可见性与唯一约束均可靠），以真实复现跨 DbContext 的乐观并发冲突。
/// </summary>
public sealed class OptimisticConcurrencyTests : IDisposable
{
    // 文件型 SQLite：两个独立 DbContext 共享同一库，已提交数据跨连接可见，可复现乐观并发冲突。
    private readonly string _dbFile = Path.Combine(Path.GetTempPath(), $"fe_occ_{Guid.NewGuid():N}.db");
    private readonly List<FlowEngineDbContext> _contexts = new();

    public OptimisticConcurrencyTests()
    {
        try
        {
            if (File.Exists(_dbFile))
            {
                File.Delete(_dbFile);
            }
        }
        catch (IOException)
        {
            // 忽略删除失败（如仍被占用），后续 EnsureCreated 会覆盖。
        }

        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        foreach (var context in _contexts)
        {
            context.Dispose();
        }

        try
        {
            if (File.Exists(_dbFile))
            {
                File.Delete(_dbFile);
            }
        }
        catch (IOException)
        {
            // 忽略删除失败，不影响测试结论。
        }
    }

    [Fact]
    public async Task Workflow_ConcurrentUpdate_SecondWriterThrowsConcurrencyException()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        await SeedWorkflowAsync(ct, id);

        // 两个独立上下文加载同一行（均读到 RowVersion 初始值）。
        var ctx1 = CreateContext();
        var ctx2 = CreateContext();
        var w1 = await ctx1.Workflows.FindAsync(new object[] { id }, ct);
        var w2 = await ctx2.Workflows.FindAsync(new object[] { id }, ct);

        // 先写者成功提交，并自增 RowVersion。
        w1!.Name = "edited-by-context-1";
        await ctx1.SaveChangesAsync(ct);

        // 后写者基于过期 RowVersion 提交，必须被乐观并发检测拦截。
        w2!.Name = "edited-by-context-2";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctx2.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Project_ConcurrentUpdate_SecondWriterThrowsConcurrencyException()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        await SeedProjectAsync(ct, id);

        var ctx1 = CreateContext();
        var ctx2 = CreateContext();
        var p1 = await ctx1.Projects.FindAsync(new object[] { id }, ct);
        var p2 = await ctx2.Projects.FindAsync(new object[] { id }, ct);

        p1!.Name = "edited-by-context-1";
        await ctx1.SaveChangesAsync(ct);

        p2!.Name = "edited-by-context-2";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctx2.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Credential_ConcurrentUpdate_SecondWriterThrowsConcurrencyException()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        await SeedCredentialAsync(ct, id);

        var ctx1 = CreateContext();
        var ctx2 = CreateContext();
        var c1 = await ctx1.Credentials.FindAsync(new object[] { id }, ct);
        var c2 = await ctx2.Credentials.FindAsync(new object[] { id }, ct);

        c1!.Name = "edited-by-context-1";
        await ctx1.SaveChangesAsync(ct);

        c2!.Name = "edited-by-context-2";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctx2.SaveChangesAsync(ct));
    }

    private async Task SeedWorkflowAsync(CancellationToken ct, Guid id)
    {
        using var seed = CreateContext();
        seed.Workflows.Add(new Workflow
        {
            Id = id,
            Name = "initial",
            ProjectId = Guid.NewGuid(),
            Nodes = [],
            Connections = [],
            CreatedBy = "test",
            IsActive = true,
        });
        await seed.SaveChangesAsync(ct);
    }

    private async Task SeedProjectAsync(CancellationToken ct, Guid id)
    {
        using var seed = CreateContext();
        seed.Projects.Add(new Project
        {
            Id = id,
            Name = "initial",
            CreatedBy = "test",
        });
        await seed.SaveChangesAsync(ct);
    }

    private async Task SeedCredentialAsync(CancellationToken ct, Guid id)
    {
        using var seed = CreateContext();
        seed.Credentials.Add(new Credential
        {
            Id = id,
            Name = "initial",
            Type = "apikey",
            KeyVersion = "kv1",
            ProjectId = null,
        });
        await seed.SaveChangesAsync(ct);
    }

    private FlowEngineDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseSqlite($"Data Source={_dbFile}")
            .AddInterceptors(new BusyTimeoutInterceptor())
            .Options;
        var context = new FlowEngineDbContext(options);
        _contexts.Add(context);
        return context;
    }

    /// <summary>
    /// 并发写场景下设置 SQLite 忙等待超时，避免「database is locked」误报。
    /// </summary>
    private sealed class BusyTimeoutInterceptor : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
            => Apply(connection);

        public override Task ConnectionOpenedAsync(
            DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken)
        {
            Apply(connection);
            return Task.CompletedTask;
        }

        private static void Apply(DbConnection connection)
        {
            if (connection is SqliteConnection sqlite)
            {
                using var command = sqlite.CreateCommand();
                command.CommandText = "PRAGMA busy_timeout = 10000;";
                command.ExecuteNonQuery();
            }
        }
    }
}
