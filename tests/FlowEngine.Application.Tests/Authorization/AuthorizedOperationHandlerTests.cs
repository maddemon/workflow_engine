using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using FlowEngine.Application.Tests.TestSupport.Fakes;

namespace FlowEngine.Application.Tests.Authorization;

/// <summary>
/// AuthorizedOperationHandler 授权调用序列与 null 返回语义钉死测试。
/// 验证策略字段如何映射到 IAuthorizationGuard 调用，以及 fail-fast 行为。
/// </summary>
public sealed class AuthorizedOperationHandlerTests
{
    private readonly FakeAuthorizationGuard _guard;
    private readonly CaptureEventBus _eventBus;
    private readonly AuthorizedOperationHandler _handler;

    public AuthorizedOperationHandlerTests()
    {
        _guard = new FakeAuthorizationGuard();
        _eventBus = new CaptureEventBus();
        var auditFactory = new AuditEventFactory(new FakeUserContext());
        _handler = new AuthorizedOperationHandler(_guard, _eventBus, auditFactory);
    }

    [Fact]
    public async Task AuthorizePreAsync_WithResourceOnly_CallsRequireAccess()
    {
        var policy = new AuthorizationPolicy(
            ResourceKind.Workflow, Operation.Read, Scope: null, AdminPhase: false, ProjectScoped: false);
        var resourceId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        await _handler.AuthorizePreAsync(policy, resourceId, ct);

        Assert.Single(_guard.CallSequence);
        Assert.Equal($"RequireAccess:{ResourceKind.Workflow}:{Operation.Read}", _guard.CallSequence[0]);
    }

    [Fact]
    public async Task AuthorizePreAsync_WithScope_CallsRequireAccessThenRequireScope()
    {
        var policy = new AuthorizationPolicy(
            ResourceKind.Workflow, Operation.Write, Scope.Workflow, AdminPhase: false, ProjectScoped: false);
        var resourceId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        await _handler.AuthorizePreAsync(policy, resourceId, ct);

        Assert.Equal(2, _guard.CallSequence.Count);
        Assert.Equal($"RequireAccess:{ResourceKind.Workflow}:{Operation.Write}", _guard.CallSequence[0]);
        Assert.Equal($"RequireScope:{Scope.Workflow}:{Operation.Write}", _guard.CallSequence[1]);
    }

    [Fact]
    public async Task AuthorizePreAsync_WithAdminPhase_CallsRequireAccessThenRequireAdmin()
    {
        var policy = new AuthorizationPolicy(
            ResourceKind.Workflow, Operation.Delete, Scope: null, AdminPhase: true, ProjectScoped: false);
        var resourceId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        await _handler.AuthorizePreAsync(policy, resourceId, ct);

        Assert.Equal(2, _guard.CallSequence.Count);
        Assert.Equal($"RequireAccess:{ResourceKind.Workflow}:{Operation.Delete}", _guard.CallSequence[0]);
        Assert.Equal($"RequireAdmin:{Operation.Delete}", _guard.CallSequence[1]);
    }

    [Fact]
    public async Task AuthorizePreAsync_WhenRequireAccessFails_DoesNotCallRequireScope()
    {
        var policy = new AuthorizationPolicy(
            ResourceKind.Workflow, Operation.Write, Scope.Workflow, AdminPhase: false, ProjectScoped: false);
        _guard.FailOnRequireAccess = true;
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<PermissionDeniedException>(() => _handler.AuthorizePreAsync(policy, Guid.NewGuid(), ct));

        Assert.Single(_guard.CallSequence);
        Assert.DoesNotContain(_guard.CallSequence, c => c.StartsWith("RequireScope:"));
    }

    [Fact]
    public async Task AuthorizePreAsync_WhenRequireAccessFails_DoesNotCallRequireAdmin()
    {
        var policy = new AuthorizationPolicy(
            ResourceKind.Workflow, Operation.Delete, Scope: null, AdminPhase: true, ProjectScoped: false);
        _guard.FailOnRequireAccess = true;
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<PermissionDeniedException>(() => _handler.AuthorizePreAsync(policy, Guid.NewGuid(), ct));

        Assert.Single(_guard.CallSequence);
        Assert.DoesNotContain(_guard.CallSequence, c => c.StartsWith("RequireAdmin:"));
    }

    [Fact]
    public async Task AuthorizePreAsync_WhenRequireScopeFails_DoesNotCallRequireAdmin()
    {
        var policy = new AuthorizationPolicy(
            ResourceKind.Workflow, Operation.Delete, Scope.Workflow, AdminPhase: true, ProjectScoped: false);
        _guard.FailOnRequireScope = true;
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<PermissionDeniedException>(() => _handler.AuthorizePreAsync(policy, Guid.NewGuid(), ct));

        Assert.Equal(2, _guard.CallSequence.Count);
        Assert.DoesNotContain(_guard.CallSequence, c => c.StartsWith("RequireAdmin:"));
    }

    [Fact]
    public async Task AuthorizePreAsync_WithAdminOnly_NoResource_DoesNotCallRequireAccess()
    {
        var policy = new AuthorizationPolicy(
            Resource: null, Access: Operation.Delete, Scope: null, AdminPhase: true, ProjectScoped: false);
        var ct = TestContext.Current.CancellationToken;

        await _handler.AuthorizePreAsync(policy, Guid.NewGuid(), ct);

        Assert.Single(_guard.CallSequence);
        Assert.Equal($"RequireAdmin:{Operation.Delete}", _guard.CallSequence[0]);
    }

    [Fact]
    public async Task AuthorizeProjectAccessAsync_CallsRequireAccessWithProjectKind()
    {
        var projectId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        await _handler.AuthorizeProjectAccessAsync(projectId, Operation.Write, ct);

        Assert.Single(_guard.CallSequence);
        Assert.Equal($"RequireAccess:{ResourceKind.Project}:{Operation.Write}", _guard.CallSequence[0]);
    }

    [Fact]
    public async Task AuthorizePreAsync_WithProjectScoped_CallsRequireAccessWithProjectKind()
    {
        var policy = new AuthorizationPolicy(
            Resource: null, Access: Operation.Write, Scope: null, AdminPhase: false, ProjectScoped: true);
        var projectId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        await _handler.AuthorizePreAsync(policy, projectId, ct);

        Assert.Single(_guard.CallSequence);
        Assert.Equal($"RequireAccess:{ResourceKind.Project}:{Operation.Write}", _guard.CallSequence[0]);
    }

    [Fact]
    public async Task AuthorizePreAsync_WithProjectScopedAndResource_DoesNotDoubleCheckProject()
    {
        // 同时设置 Resource 与 ProjectScoped 时，应分别执行资源访问检查与项目级检查。
        var policy = new AuthorizationPolicy(
            Resource: ResourceKind.Workflow, Access: Operation.Write, Scope: null, AdminPhase: false, ProjectScoped: true);
        var resourceId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        await _handler.AuthorizePreAsync(policy, resourceId, ct);

        Assert.Equal(2, _guard.CallSequence.Count);
        Assert.Contains($"RequireAccess:{ResourceKind.Workflow}:{Operation.Write}", _guard.CallSequence);
        Assert.Contains($"RequireAccess:{ResourceKind.Project}:{Operation.Write}", _guard.CallSequence);
    }

    [Fact]
    public async Task PublishAuditAsync_PublishesEventWithCorrectPayload()
    {
        var resourceId = Guid.NewGuid();
        var payload = new Dictionary<string, object> { ["name"] = "test-resource" };
        var ct = TestContext.Current.CancellationToken;

        await _handler.PublishAuditAsync(
            AuditEventTypes.WorkflowUpdated,
            "Workflow",
            resourceId,
            payload,
            ct);

        Assert.Single(_eventBus.PublishedEvents);
        var published = Assert.IsType<AuditLogEvent>(_eventBus.PublishedEvents[0]);
        Assert.Equal(AuditEventTypes.WorkflowUpdated, published.EventType);
        Assert.Equal("Workflow", published.ResourceType);
        Assert.Equal(resourceId, published.ResourceId);
        Assert.NotNull(published.Payload);
        Assert.Equal("test-resource", published.Payload!["name"].ToString());
    }

    [Fact]
    public async Task AuthorizePreAsync_WhenAllChecksPass_DoesNotThrow()
    {
        var policy = new AuthorizationPolicy(
            ResourceKind.Workflow, Operation.Delete, Scope.Workflow, AdminPhase: true, ProjectScoped: false);
        var ct = TestContext.Current.CancellationToken;

        var exception = await Record.ExceptionAsync(() => _handler.AuthorizePreAsync(policy, Guid.NewGuid(), ct));

        Assert.Null(exception);
        Assert.Equal(3, _guard.CallSequence.Count);
    }

    private sealed class FakeAuthorizationGuard : IAuthorizationGuard
    {
        public List<string> CallSequence { get; } = [];
        public bool FailOnRequireAccess { get; set; }
        public bool FailOnRequireScope { get; set; }
        public bool FailOnRequireAdmin { get; set; }

        public Task RequireAccessAsync(ResourceKind kind, Guid resourceId, Operation operation, CancellationToken ct = default)
        {
            CallSequence.Add($"RequireAccess:{kind}:{operation}");
            if (FailOnRequireAccess)
            {
                throw new PermissionDeniedException("access denied");
            }
            return Task.CompletedTask;
        }

        public Task RequireScopeAsync(Scope scope, Operation operation, CancellationToken ct = default)
        {
            CallSequence.Add($"RequireScope:{scope}:{operation}");
            if (FailOnRequireScope)
            {
                throw new PermissionDeniedException("scope denied");
            }
            return Task.CompletedTask;
        }

        public Task RequireAdminAsync(Operation operation, CancellationToken ct = default)
        {
            CallSequence.Add($"RequireAdmin:{operation}");
            if (FailOnRequireAdmin)
            {
                throw new PermissionDeniedException("admin denied");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class CaptureEventBus : IEventBus
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

}
