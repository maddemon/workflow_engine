using System.Threading.Channels;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 工作流执行队列工作项。
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
