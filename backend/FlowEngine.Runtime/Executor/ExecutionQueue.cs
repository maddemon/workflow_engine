using System.Threading.Channels;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 基于 Channel 的 FIFO 执行队列。
/// </summary>
public sealed class ExecutionQueue
{
    private readonly Channel<NodeWorkItem> _channel;

    /// <summary>
    /// 初始化执行队列。
    /// </summary>
    public ExecutionQueue()
    {
        _channel = Channel.CreateUnbounded<NodeWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// 入队一个节点工作项。
    /// </summary>
    /// <param name="item">工作项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task EnqueueAsync(NodeWorkItem item, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 尝试出队一个节点工作项。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工作项。</returns>
    public async Task<NodeWorkItem> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取队列读取器。
    /// </summary>
    public ChannelReader<NodeWorkItem> Reader => _channel.Reader;

    /// <summary>
    /// 标记队列完成，不再接受新项。
    /// </summary>
    public void Complete() => _channel.Writer.Complete();
}
