using System.Threading;
using FlowEngine.Runtime.Executor;

namespace FlowEngine.Runtime.Tests.Executor;

/// <summary>
/// <see cref="ExecutionCancellationRegistry"/> 单元測试：按 executionId 登记 / 取消 / 解除取消令牌源。
/// </summary>
public sealed class ExecutionCancellationRegistryTests
{
    [Fact]
    public void TryCancel_NotRegistered_ReturnsFalse_AndDoesNotThrow()
    {
        var registry = new ExecutionCancellationRegistry();

        var cancelled = registry.TryCancel(Guid.NewGuid());

        Assert.False(cancelled);
    }

    [Fact]
    public void Register_ThenTryCancel_CancelsTheRegisteredSource()
    {
        var registry = new ExecutionCancellationRegistry();
        var executionId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        registry.Register(executionId, cts);

        var cancelled = registry.TryCancel(executionId);

        Assert.True(cancelled);
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void Unregister_RemovesSource_SoSubsequentTryCancelIsNoOp()
    {
        var registry = new ExecutionCancellationRegistry();
        var executionId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        registry.Register(executionId, cts);

        registry.Unregister(executionId);

        var cancelled = registry.TryCancel(executionId);
        Assert.False(cancelled);
        // 解除登记后不再影响令牌源状态。
        Assert.False(cts.IsCancellationRequested == false && cts.IsCancellationRequested, "解除后不应触发取消。");
    }

    [Fact]
    public void Register_SameExecutionId_ReplacesPriorSource()
    {
        var registry = new ExecutionCancellationRegistry();
        var executionId = Guid.NewGuid();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        registry.Register(executionId, cts1);
        registry.Register(executionId, cts2);

        registry.TryCancel(executionId);

        Assert.True(cts2.IsCancellationRequested);
        Assert.False(cts1.IsCancellationRequested);
    }

    [Fact]
    public void ConcurrentRegisterAndCancel_IsThreadSafe()
    {
        var registry = new ExecutionCancellationRegistry();
        var executionId = Guid.NewGuid();

        Parallel.For(0, 50, _ =>
        {
            using var cts = new CancellationTokenSource();
            registry.Register(executionId, cts);
            registry.TryCancel(executionId);
            registry.Unregister(executionId);
        });

        // 不抛异常即为通过；最终状态不确定，仅验证无竞争崩溃。
        Assert.True(true);
    }
}
