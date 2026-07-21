#pragma warning disable xUnit1051 // Use TestContext.Current.CancellationToken

using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Executions;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Tests.TestSupport.Fakes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Runtime.Executor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Application.Tests.Executions;

/// <summary>
/// <see cref="ExecutionService.CancelAsync"/> 行为测试（修复 #1：取消执行是空操作）。
/// 覆盖正常路径（Pending 直接落库 Cancelled）、边界（不存在 / 已终态冲突），
/// 以及运行中执行经 <see cref="ExecutionCancellationRegistry"/> 真正触发取消信号（worker 据此落库 Cancelled）。
/// </summary>
public sealed class ExecutionCancelTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly ExecutionCancellationRegistry _registry;
    private readonly ExecutionService _service;

    public ExecutionCancelTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase($"CancelTestDb_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new FlowEngineDbContext(options);

        var userContext = new FakeUserContext { Roles = [RoleConstants.Admin] };
        var eventBus = new RecordingEventBus();
        var auditFactory = new AuditEventFactory(userContext);
        var authGuard = AuthorizationGuardFactory.Create(
            userContext, new AllowAllResourceAuth(), eventBus);
        _registry = new ExecutionCancellationRegistry();
        _service = new ExecutionService(
            new NullEngine(),
            _dbContext,
            new NullIdempotencyService(),
            authGuard,
            eventBus,
            auditFactory,
            _registry);
    }

    public void Dispose() => _dbContext.Dispose();

    // 边界：取消不存在的执行应返回 (null, false)，不抛异常。
    [Fact]
    public async Task CancelAsync_NonExistentExecution_ReturnsNullWithoutConflict()
    {
        var (execution, conflict) = await _service.CancelAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Null(execution);
        Assert.False(conflict);
    }

    // 正常路径：Pending 执行尚未被 worker 取出，CancelAsync 直接落库 Cancelled，返回对应 DTO。
    [Fact]
    public async Task CancelAsync_PendingExecution_PersistsCancelledAndReturnsDto()
    {
        var id = await SeedExecutionAsync(ExecutionStatus.Pending);

        var (execution, conflict) = await _service.CancelAsync(id, TestContext.Current.CancellationToken);

        Assert.False(conflict);
        Assert.NotNull(execution);
        Assert.Equal("Cancelled", execution!.Status);

        var persisted = await _dbContext.ExecutionRecords.FindAsync(id);
        Assert.Equal(ExecutionStatus.Cancelled, persisted!.Status);
        Assert.NotNull(persisted.CompletedAt);
    }

    // 边界：已终态（Completed）执行不可取消，返回 (dto, conflict=true)。
    [Fact]
    public async Task CancelAsync_CompletedExecution_ReturnsConflict()
    {
        var id = await SeedExecutionAsync(ExecutionStatus.Completed);

        var (execution, conflict) = await _service.CancelAsync(id, TestContext.Current.CancellationToken);

        Assert.True(conflict);
        Assert.NotNull(execution);
        Assert.Equal("Completed", execution!.Status);

        var persisted = await _dbContext.ExecutionRecords.FindAsync(id);
        Assert.Equal(ExecutionStatus.Completed, persisted!.Status);
    }

    // 机制验证：运行中执行经注册表登记的 CTS 被 CancelAsync 真正取消
    // （worker 循环检测到 IsCancellationRequested 后走 StateMachine.Cancel() 落库 Cancelled）。
    [Fact]
    public async Task CancelAsync_RunningExecution_WithRegisteredCts_SignalsCancellationToWorker()
    {
        var id = await SeedExecutionAsync(ExecutionStatus.Running);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        _registry.Register(id, cts);

        var (execution, conflict) = await _service.CancelAsync(id, TestContext.Current.CancellationToken);

        // 仅当 worker 尚未进入终态时才由 CancelAsync 落库；Running 状态下保持 Running 交由 worker 处理，
        // 但注册表中的取消源必须已被触发。
        Assert.False(conflict);
        Assert.True(cts.IsCancellationRequested, "CancelAsync 应经注册表取消运行中执行的令牌源。");
        Assert.Equal("Running", execution!.Status);

        _registry.Unregister(id);
    }

    private async Task<Guid> SeedExecutionAsync(ExecutionStatus status)
    {
        var record = new ExecutionRecord
        {
            WorkflowDefinitionId = Guid.NewGuid(),
            StartedAt = DateTime.UtcNow,
            Status = status,
            NodeRecords = [],
        };
        _dbContext.ExecutionRecords.Add(record);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return record.Id;
    }

    private sealed class NullEngine : IEngine
    {
        public Task<ExecutionId> StartAsync(
            Guid workflowDefinitionId, object? triggerPayload = null, CancellationToken cancellationToken = default)
            => Task.FromResult(ExecutionId.From(Guid.NewGuid()));
    }

    private sealed class NullIdempotencyService : IExecutionIdempotencyService
    {
        public Task<Guid?> TryGetOrRegisterAsync(string idempotencyKey, Guid executionId, TimeSpan? ttl = null, CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);

        public Task<Guid?> TryGetExistingAsync(string idempotencyKey, CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);

        public Task CleanupExpiredAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class AllowAllResourceAuth : IResourceAuthorizationService
    {
        public Task<bool> CanAccessWorkflowAsync(Guid userId, Guid workflowId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CanAccessCredentialAsync(Guid userId, Guid credentialId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CanAccessExecutionAsync(Guid userId, Guid executionId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CanAccessTriggerAsync(Guid userId, Guid triggerId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public bool ShouldMaskCredentialValues(IReadOnlyList<string> roles) => false;
    }
}
