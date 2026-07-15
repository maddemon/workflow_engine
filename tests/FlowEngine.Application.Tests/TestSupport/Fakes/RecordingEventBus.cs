using FlowEngine.Core.Abstractions;

namespace FlowEngine.Application.Tests.TestSupport.Fakes;

public sealed class RecordingEventBus : IEventBus
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
