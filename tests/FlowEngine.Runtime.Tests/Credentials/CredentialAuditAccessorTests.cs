using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Events;
using FlowEngine.Runtime.Credentials;
using Xunit;

namespace FlowEngine.Runtime.Tests.Credentials;

/// <summary>
/// 验证 OBS-1：运行时凭据解析（解密）成功后，<see cref="CredentialAuditAccessor"/> 发布
/// <see cref="CredentialAccessedEvent"/>，闭合凭据访问审计链，且事件绝不携带凭据明文。
/// </summary>
public sealed class CredentialAuditAccessorTests
{
    private sealed class StubAccessor : ICredentialAccessor
    {
        private readonly CredentialValue? _value;

        public StubAccessor(CredentialValue? value) => _value = value;

        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult(_value ?? new CredentialValue());

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(_value);
    }

    private sealed class CapturingEventBus : IEventBus
    {
        public List<object> Published { get; } = new();

        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            Published.Add(eventInstance!);
            return Task.CompletedTask;
        }

        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : IDomainEvent
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task GetCredentialAsync_PublishesCredentialAccessedEvent_WithoutPlaintext()
    {
        var credentialId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var nodeDefinitionId = "node-abc";
        var secret = "top-secret-value";

        var inner = new StubAccessor(new CredentialValue
        {
            Id = credentialId,
            Name = "db",
            Fields = new Dictionary<string, string> { ["password"] = secret },
        });
        var eventBus = new CapturingEventBus();

        var accessor = new CredentialAuditAccessor(inner, eventBus, executionId, nodeDefinitionId);
        var value = await accessor.GetCredentialAsync(credentialId, CancellationToken.None);

        Assert.NotNull(value);
        var evt = Assert.Single(eventBus.Published.OfType<CredentialAccessedEvent>());
        Assert.Equal(credentialId, evt.CredentialId);
        Assert.Equal(executionId, evt.ExecutionId);
        Assert.Equal(nodeDefinitionId, evt.NodeDefinitionId);
        Assert.Equal("Resolve", evt.AccessType);

        // 凭据明文绝不得出现在审计事件中。
        Assert.DoesNotContain(secret, evt.ToString());
    }

    [Fact]
    public async Task GetCredentialByNameAsync_PublishesCredentialAccessedEvent()
    {
        var credentialId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var inner = new StubAccessor(new CredentialValue { Id = credentialId, Name = "api" });
        var eventBus = new CapturingEventBus();

        var accessor = new CredentialAuditAccessor(inner, eventBus, executionId, "node-x");
        await accessor.GetCredentialByNameAsync("api", CancellationToken.None);

        var evt = Assert.Single(eventBus.Published.OfType<CredentialAccessedEvent>());
        Assert.Equal(credentialId, evt.CredentialId);
        Assert.Equal("Resolve", evt.AccessType);
    }

    [Fact]
    public async Task MissingCredential_DoesNotPublish()
    {
        var inner = new StubAccessor(null);
        var eventBus = new CapturingEventBus();

        var accessor = new CredentialAuditAccessor(inner, eventBus, Guid.NewGuid(), "node-x");
        var value = await accessor.GetCredentialByNameAsync("missing", CancellationToken.None);

        Assert.Null(value);
        Assert.Empty(eventBus.Published);
    }
}
