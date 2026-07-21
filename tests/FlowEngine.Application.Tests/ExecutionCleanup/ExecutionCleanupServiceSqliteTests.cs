using FlowEngine.Application.ExecutionCleanup;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using FlowEngine.Application.Tests.TestSupport.Fakes;

namespace FlowEngine.Application.Tests.ExecutionCleanup;

/// <summary>
/// 关系型（SQLite 内存库）集成测试，专门覆盖 ExecutionCleanupService 的
/// <c>ExecuteDeleteAsync</c> 批量删除路径（<c>dbContext.Database.IsRelational() == true</c>）。
/// 既有测试全部使用 InMemory 提供程序，<c>IsRelational()</c> 为 false，永远走不到关系型分支；
/// 本测试通过内存 SQLite 将 <c>IsRelational()</c> 翻为 true，并用 EF Core 命令日志证明
/// 实际执行了单语句批量删除，而非 InMemory 的退化路径。
/// </summary>
public sealed class ExecutionCleanupServiceSqliteTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly RecordingEventBus _eventBus;
    private readonly ExecutionCleanupOptions _options;
    private readonly ExecutionCleanupService _service;
    private readonly List<string> _log = [];

    public ExecutionCleanupServiceSqliteTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseSqlite("DataSource=:memory:") // IsRelational() == true
            .LogTo(msg => _log.Add(msg), LogLevel.Information) // 捕获 EF Core 命令 SQL
            .Options;

        _dbContext = new FlowEngineDbContext(options);
        _dbContext.Database.OpenConnection(); // 必须保持一个连接打开，内存 SQLite 才存活
        _dbContext.Database.EnsureCreated();

        _eventBus = new RecordingEventBus();
        _options = new ExecutionCleanupOptions
        {
            Enabled = true,
            IntervalMinutes = 60,
            RetentionDays = 30,
            MaxRecordsToKeep = 10000,
        };
        _service = new ExecutionCleanupService(
            _dbContext,
            Options.Create(_options),
            _eventBus,
            new StubIdempotencyService(),
            NullLogger<ExecutionCleanupService>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task CleanupAsync_RelationalExecuteDelete_RemovesExpiredAndKeepsRecent()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.NewGuid();

        // 多条过期终态记录：应被一次批量删除（用多条以区分 ExecuteDelete 单语句与退化路径逐行删除）
        var expired1 = AddExecutionRecord(workflowId, ExecutionStatus.Completed, DateTime.UtcNow.AddDays(-31));
        var expired2 = AddExecutionRecord(workflowId, ExecutionStatus.Failed, DateTime.UtcNow.AddDays(-45));
        var expired3 = AddExecutionRecord(workflowId, ExecutionStatus.Completed, DateTime.UtcNow.AddDays(-60));
        // 近期终态记录：保留天数内，应保留
        var recent = AddExecutionRecord(workflowId, ExecutionStatus.Completed, DateTime.UtcNow.AddDays(-10));
        // 非终态记录（运行中）：清理只删终态，应保留
        var running = AddExecutionRecord(workflowId, ExecutionStatus.Running, null);

        // 仅捕获 CleanupAsync 期间产生的命令（建表 / 插入已在之前发生）
        _log.Clear();
        await _service.CleanupAsync(ct);

        // ExecuteDeleteAsync 不会同步清理变更跟踪器中的实体，重新查询前先清空
        _dbContext.ChangeTracker.Clear();
        var remaining = await _dbContext.ExecutionRecords.ToListAsync(ct);

        Assert.DoesNotContain(remaining, r => r.Id == expired1.Id);
        Assert.DoesNotContain(remaining, r => r.Id == expired2.Id);
        Assert.DoesNotContain(remaining, r => r.Id == expired3.Id);
        Assert.Contains(remaining, r => r.Id == recent.Id);
        Assert.Contains(remaining, r => r.Id == running.Id);

        // 关系型 ExecuteDeleteAsync 只生成「一条」DELETE 语句（无论一次删多少行）。
        // 若是 InMemory 退化路径，根本不会产生任何 SQL DELETE（全程无 SQL）；
        // 即便退化路径跑在关系型提供程序上，RemoveRange 也会逐行生成 N 条 DELETE。
        var deleteCount = _log.Count(l => l.Contains("DELETE FROM") && l.Contains("execution_records"));
        Assert.Equal(1, deleteCount);
    }

    private ExecutionRecord AddExecutionRecord(Guid workflowId, ExecutionStatus status, DateTime? completedAt)
    {
        var record = new ExecutionRecord
        {
            WorkflowDefinitionId = workflowId,
            Status = status,
            StartedAt = completedAt?.AddDays(-1) ?? DateTime.UtcNow.AddDays(-1),
            CompletedAt = completedAt,
        };
        _dbContext.ExecutionRecords.Add(record);
        _dbContext.SaveChanges();
        return record;
    }

    private sealed class StubIdempotencyService : IExecutionIdempotencyService
    {
        public Task<Guid?> TryGetOrRegisterAsync(string idempotencyKey, Guid executionId, TimeSpan? ttl = null, CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);

        public Task<Guid?> TryGetExistingAsync(string idempotencyKey, CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);

        public Task CleanupExpiredAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
