using FlowEngine.Application.ExecutionCleanup;
using FlowEngine.Application.Executions;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlowEngine.Application.Tests.ExecutionCleanup;

/// <summary>
/// ExecutionCleanupService 清理逻辑测试（GAP-22）。
/// 验证保留天数、最大记录数、禁用开关与终态过滤行为。
/// </summary>
public sealed class ExecutionCleanupServiceTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly InMemoryEventBus _eventBus;
    private readonly ExecutionCleanupOptions _options;
    private readonly ExecutionCleanupService _service;

    public ExecutionCleanupServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _eventBus = new InMemoryEventBus();
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

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task CleanupAsync_DeletesRecordsOlderThanRetentionDays()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.NewGuid();
        var cutoff = DateTime.UtcNow.AddDays(-31);
        var recent = DateTime.UtcNow.AddDays(-10);

        var expired = AddExecutionRecord(workflowId, ExecutionStatus.Completed, cutoff);
        var kept = AddExecutionRecord(workflowId, ExecutionStatus.Completed, recent);

        await _service.CleanupAsync(ct);

        var remaining = await _dbContext.ExecutionRecords.ToListAsync(ct);
        Assert.DoesNotContain(remaining, r => r.Id == expired.Id);
        Assert.Contains(remaining, r => r.Id == kept.Id);
    }

    [Fact]
    public async Task CleanupAsync_KeepsOnlyMaxRecordsPerWorkflow()
    {
        var ct = TestContext.Current.CancellationToken;
        _options.MaxRecordsToKeep = 3;
        var workflowId = Guid.NewGuid();
        var baseTime = DateTime.UtcNow.AddDays(-1);

        var r1 = AddExecutionRecord(workflowId, ExecutionStatus.Completed, baseTime.AddMinutes(-1));
        var r2 = AddExecutionRecord(workflowId, ExecutionStatus.Completed, baseTime.AddMinutes(-2));
        var r3 = AddExecutionRecord(workflowId, ExecutionStatus.Completed, baseTime.AddMinutes(-3));
        var r4 = AddExecutionRecord(workflowId, ExecutionStatus.Completed, baseTime.AddMinutes(-4));
        var r5 = AddExecutionRecord(workflowId, ExecutionStatus.Completed, baseTime.AddMinutes(-5));

        await _service.CleanupAsync(ct);

        var remaining = await _dbContext.ExecutionRecords.ToListAsync(ct);
        Assert.Equal(3, remaining.Count);
        Assert.Contains(remaining, r => r.Id == r1.Id);
        Assert.Contains(remaining, r => r.Id == r2.Id);
        Assert.Contains(remaining, r => r.Id == r3.Id);
        Assert.DoesNotContain(remaining, r => r.Id == r4.Id);
        Assert.DoesNotContain(remaining, r => r.Id == r5.Id);
    }

    [Fact]
    public async Task CleanupAsync_Disabled_DoesNotDeleteAnyRecords()
    {
        var ct = TestContext.Current.CancellationToken;
        _options.Enabled = false;
        var workflowId = Guid.NewGuid();
        var old = AddExecutionRecord(workflowId, ExecutionStatus.Completed, DateTime.UtcNow.AddDays(-100));

        await _service.CleanupAsync(ct);

        var remaining = await _dbContext.ExecutionRecords.ToListAsync(ct);
        Assert.Contains(remaining, r => r.Id == old.Id);
        Assert.Empty(_eventBus.PublishedEvents);
    }

    [Fact]
    public async Task CleanupAsync_OnlyDeletesTerminalStatusRecords()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.NewGuid();
        var oldTime = DateTime.UtcNow.AddDays(-100);

        var running = AddExecutionRecord(workflowId, ExecutionStatus.Running, null);
        var pending = AddExecutionRecord(workflowId, ExecutionStatus.Pending, null);
        var completed = AddExecutionRecord(workflowId, ExecutionStatus.Completed, oldTime);
        var failed = AddExecutionRecord(workflowId, ExecutionStatus.Failed, oldTime);

        await _service.CleanupAsync(ct);

        var remaining = await _dbContext.ExecutionRecords.ToListAsync(ct);
        Assert.Contains(remaining, r => r.Id == running.Id);
        Assert.Contains(remaining, r => r.Id == pending.Id);
        Assert.DoesNotContain(remaining, r => r.Id == completed.Id);
        Assert.DoesNotContain(remaining, r => r.Id == failed.Id);
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

    private sealed class InMemoryEventBus : IEventBus
    {
        public List<object> PublishedEvents { get; } = [];

        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            PublishedEvents.Add(eventInstance!);
            return Task.CompletedTask;
        }

        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : IDomainEvent => new Disposable();

        private sealed class Disposable : IDisposable
        {
            public void Dispose() { }
        }
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
