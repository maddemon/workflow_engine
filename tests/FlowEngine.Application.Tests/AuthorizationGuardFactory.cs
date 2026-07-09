using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;

namespace FlowEngine.Application.Tests;

/// <summary>
/// 测试辅助：用测试桩的 IResourceAuthorizationService / IUserContext 组装真实的 AuthorizationGuard，
/// 让服务层授权测试走与生产的 guard 相同路径（含「拒绝必审计」不变量）。
/// </summary>
internal static class AuthorizationGuardFactory
{
    public static IAuthorizationGuard Create(
        IUserContext userContext,
        IResourceAuthorizationService resourceAuth,
        IEventBus? eventBus = null)
        => new AuthorizationGuard(
            userContext,
            resourceAuth,
            new AuthorizationService(),
            eventBus ?? new NullEventBus(),
            new AuditEventFactory(userContext));

    /// <summary>
    /// 无副作用的事件总线：丢弃所有事件，供仅断言异常的授权测试使用。
    /// </summary>
    private sealed class NullEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent => Task.CompletedTask;

        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : IDomainEvent => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
