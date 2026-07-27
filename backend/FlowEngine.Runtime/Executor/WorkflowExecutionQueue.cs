using System.Threading.Channels;
using FlowEngine.Core.Entities;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 工作流执行队列工作项。仅携带工作流定义 ID，不携带任何已加载的工作流实体。
/// 后台 worker 在各自独立的执行作用域内依据 <see cref="WorkflowDefinitionId"/> 重新加载工作流，
/// 避免复用来自请求作用域（不同 <see cref="FlowEngineDbContext"/> ChangeTracker）的实体，
/// 从而消除跨 DbContext 作用域共享实体导致的重复插入 / Detached 异常。
/// </summary>
public sealed record WorkflowExecutionWorkItem(
    Guid ExecutionRecordId,
    Guid WorkflowDefinitionId,
    object? TriggerPayload);

/// <summary>
/// 跨进程共享的工作流执行队列（Singleton），解耦请求入口与后台执行。
/// </summary>
public sealed class WorkflowExecutionQueue
{
    private readonly Channel<WorkflowExecutionWorkItem> _channel =
        Channel.CreateUnbounded<WorkflowExecutionWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public async Task EnqueueAsync(WorkflowExecutionWorkItem item, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WorkflowExecutionWorkItem> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Complete() => _channel.Writer.TryComplete();
}
