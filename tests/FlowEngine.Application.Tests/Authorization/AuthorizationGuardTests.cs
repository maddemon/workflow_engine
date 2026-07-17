#pragma warning disable xUnit1051 // Use TestContext.Current.CancellationToken

using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Tests.TestSupport.Fakes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using Xunit;

namespace FlowEngine.Application.Tests.Authorization;

/// <summary>
/// <see cref="AuthorizationGuard"/> 测试，覆盖 RequireAccessAsync / RequireScopeAsync / RequireAdminAsync
/// 的「放行」「拒绝（角色不足 / 归属不符）」「未认证」三类路径，并验证拒绝必审计不变量。
/// </summary>
public sealed class AuthorizationGuardTests
{
    private readonly FakeUserContext _userContext = new();
    private readonly RecordingEventBus _eventBus = new();

    private AuthorizationGuard CreateGuard(IResourceAuthorizationService? resourceAuth = null)
        => new(
            _userContext,
            resourceAuth ?? new RoleBasedResourceAuthorizationService(_userContext),
            new AuthorizationService(),
            _eventBus,
            new AuditEventFactory(_userContext));

    // ── RequireAccessAsync ─────────────────────────────────────

    [Fact]
    public async Task RequireAccessAsync_Unauthenticated_ThrowsUnauthorizedException()
    {
        _userContext.UserId = null;
        var guard = CreateGuard();

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => guard.RequireAccessAsync(ResourceKind.Workflow, Guid.NewGuid(), Operation.Read));
    }

    [Fact]
    public async Task RequireAccessAsync_Allowed_DoesNotThrowOrAudit()
    {
        _userContext.Roles = [RoleConstants.Viewer]; // Viewer 拥有 Workflow.Read
        var guard = CreateGuard();

        await guard.RequireAccessAsync(ResourceKind.Workflow, Guid.NewGuid(), Operation.Read);

        Assert.DoesNotContain(_eventBus.PublishedEvents, e => e is AuditLogEvent);
    }

    [Fact]
    public async Task RequireAccessAsync_DeniedByRole_PublishesAuditAndThrows()
    {
        // Viewer 仅有读权限，对 Write 操作角色不足 → DeniedByRole
        _userContext.Roles = [RoleConstants.Viewer];
        var guard = CreateGuard();

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(
            () => guard.RequireAccessAsync(ResourceKind.Workflow, Guid.NewGuid(), Operation.Write));

        Assert.Contains("权限", ex.Message);
        var denied = _eventBus.PublishedEvents.OfType<AuditLogEvent>()
            .First(e => e.EventType == AuditEventTypes.PermissionDenied);
        Assert.Equal("Workflow", denied.ResourceType);
        Assert.Equal("role", denied.Payload!["reason"].ToString());
    }

    [Fact]
    public async Task RequireAccessAsync_DeniedByOwnership_PublishesAuditWithOwnershipReason()
    {
        _userContext.Roles = [RoleConstants.Viewer];
        var guard = CreateGuard(new OwnershipDeniedResourceAuth());

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => guard.RequireAccessAsync(ResourceKind.Workflow, Guid.NewGuid(), Operation.Read));

        var denied = _eventBus.PublishedEvents.OfType<AuditLogEvent>()
            .First(e => e.EventType == AuditEventTypes.PermissionDenied);
        Assert.Equal("ownership", denied.Payload!["reason"].ToString());
    }

    // ── RequireScopeAsync ──────────────────────────────────────

    [Fact]
    public async Task RequireScopeAsync_Unauthenticated_ThrowsUnauthorizedException()
    {
        _userContext.UserId = null;
        var guard = CreateGuard();

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => guard.RequireScopeAsync(Scope.Workflow, Operation.Write));
    }

    [Fact]
    public async Task RequireScopeAsync_PermissionGranted_DoesNotThrowOrAudit()
    {
        _userContext.Roles = [RoleConstants.Editor]; // Editor 拥有 Workflow.Write
        var guard = CreateGuard();

        await guard.RequireScopeAsync(Scope.Workflow, Operation.Write);

        Assert.DoesNotContain(_eventBus.PublishedEvents, e => e is AuditLogEvent);
    }

    [Fact]
    public async Task RequireScopeAsync_PermissionDenied_PublishesAuditAndThrows()
    {
        // Viewer 无 Workflow.Write 作用域权限
        _userContext.Roles = [RoleConstants.Viewer];
        var guard = CreateGuard();

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => guard.RequireScopeAsync(Scope.Workflow, Operation.Write));

        var denied = _eventBus.PublishedEvents.OfType<AuditLogEvent>()
            .First(e => e.EventType == AuditEventTypes.PermissionDenied);
        Assert.Equal("Workflow", denied.ResourceType);
        Assert.Equal(Guid.Empty, denied.ResourceId);
        Assert.Equal("role", denied.Payload!["reason"].ToString());
    }

    // ── RequireAdminAsync ──────────────────────────────────────

    [Fact]
    public async Task RequireAdminAsync_Unauthenticated_ThrowsUnauthorizedException()
    {
        _userContext.UserId = null;
        var guard = CreateGuard();

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => guard.RequireAdminAsync(Operation.Delete));
    }

    [Fact]
    public async Task RequireAdminAsync_Admin_DoesNotThrowOrAudit()
    {
        _userContext.Roles = [RoleConstants.Admin];
        var guard = CreateGuard();

        await guard.RequireAdminAsync(Operation.Delete);

        Assert.DoesNotContain(_eventBus.PublishedEvents, e => e is AuditLogEvent);
    }

    [Fact]
    public async Task RequireAdminAsync_NonAdmin_PublishesAuditAndThrows()
    {
        _userContext.Roles = [RoleConstants.Viewer]; // 非 Admin
        var guard = CreateGuard();

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => guard.RequireAdminAsync(Operation.Delete));

        var denied = _eventBus.PublishedEvents.OfType<AuditLogEvent>()
            .First(e => e.EventType == AuditEventTypes.PermissionDenied);
        Assert.Equal("System", denied.ResourceType);
        Assert.Equal(Guid.Empty, denied.ResourceId);
        Assert.Equal("role", denied.Payload!["reason"].ToString());
    }

    /// <summary>
    /// 资源授权桩：裁定恒为「归属不符」，用于验证 AuthorizationGuard 细化审计 reason 为 ownership。
    /// </summary>
    private sealed class OwnershipDeniedResourceAuth : IResourceAuthorizationService
    {
        public Task<bool> CanAccessWorkflowAsync(Guid userId, Guid workflowId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> CanAccessCredentialAsync(Guid userId, Guid credentialId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> CanAccessExecutionAsync(Guid userId, Guid executionId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> CanAccessTriggerAsync(Guid userId, Guid triggerId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> CanAccessProjectAsync(Guid userId, Guid projectId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<AccessDecision> DecideAsync(Guid userId, ResourceKind kind, Guid resourceId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(AccessDecision.DeniedByOwnership);
        public bool ShouldMaskCredentialValues(IReadOnlyList<string> roles) => false;
    }
}
