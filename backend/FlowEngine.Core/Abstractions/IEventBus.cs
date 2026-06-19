namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 领域事件总线。
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// 发布领域事件。
    /// </summary>
    /// <typeparam name="TEvent">事件类型。</typeparam>
    /// <param name="eventInstance">事件实例。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;

    /// <summary>
    /// 订阅领域事件。
    /// </summary>
    /// <typeparam name="TEvent">事件类型。</typeparam>
    /// <param name="handler">事件处理函数。</param>
    /// <returns>订阅释放句柄。</returns>
    IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : IDomainEvent;
}
