using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.Scripting;
using FlowEngine.Host;
using FlowEngine.Runtime.Credentials;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Expressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowEngine.Host.Tests.Executor;

/// <summary>
/// 验证 OBS-1 的真实 DI 装配：<see cref="NodeExecutionContextFactory"/> 经宿主注册工厂构建时，
/// 必须将真正的 <see cref="IEventBus"/> 注入，从而使运行时节点的凭据访问器被
/// <see cref="CredentialAuditAccessor"/> 包裹，凭据解析后发布 <see cref="CredentialAccessedEvent"/>。
/// 该端到端测试覆盖既有直接构造装饰器测试遗漏的生产装配缺口（production-wiring gap）。
/// </summary>
public sealed class NodeExecutionContextFactoryWiringTests
{
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

    private sealed class StubCredentialAccessor : ICredentialAccessor
    {
        private readonly CredentialValue _value;

        public StubCredentialAccessor(CredentialValue value) => _value = value;

        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult(_value);

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue?>(_value);
    }

    private sealed class StubNodeRegistry : INodeRegistry
    {
        public void Register(INodeType nodeType) => throw new NotSupportedException();
        public INodeType Get(string typeName) => throw new NotSupportedException();
        public bool TryGet(string typeName, out INodeType? nodeType)
        {
            nodeType = null;
            return false;
        }

        public IReadOnlyCollection<INodeType> GetAll() => Array.Empty<INodeType>();
        public INodeType CreateInstance(string typeName) => throw new NotSupportedException();
        public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors() => Array.Empty<NodeTypeDescriptor>();
        public NodeTypeDescriptor GetDescriptor(string typeName) => new() { TypeName = typeName };
    }

    private sealed class StubNodeType : INodeType
    {
        public string TypeName => "stub";
        public string DisplayName => "Stub";
        public string Category => "test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports => Array.Empty<PortDefinition>();
        public bool DefaultIsEntry => false;
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubLlmClientFactory : ILlmClientFactory
    {
        public ILlmClient Create(string apiKey, string model, float temperature = 0.7f, int? maxTokens = null, Uri? baseEndpoint = null)
            => null!;
    }

    private sealed class StubOAuth2TokenService : IOAuth2TokenService
    {
        public Task<OAuth2TokenResponse> GetTokenAsync(OAuth2TokenRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OAuth2TokenResponse> GetOrRefreshTokenAsync(string cacheKey, OAuth2TokenRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task ResolveViaDi_WrapsCredentialAccessor_AndPublishesCredentialAccessedEvent()
    {
        // 装配最小依赖，复用宿主生产注册工厂（AddNodeExecutionContextFactory）。
        var credentialId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var nodeDefinitionId = "node-1";
        var eventBus = new CapturingEventBus();

        var credentialValue = new CredentialValue
        {
            Id = credentialId,
            Name = "MyApiKey",
            Fields = new Dictionary<string, string> { ["token"] = "top-secret" },
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton<INodeRegistry>(new StubNodeRegistry());
        services.AddSingleton<IOptions<JsEngineOptions>>(_ => Microsoft.Extensions.Options.Options.Create(new JsEngineOptions()));
        services.AddSingleton<ScriptCache>();
        services.AddScoped<ParameterResolver>();
        services.AddScoped<ICredentialAccessor>(_ => new StubCredentialAccessor(credentialValue));
        services.AddScoped<IOAuth2TokenService>(_ => new StubOAuth2TokenService());
        services.AddScoped<ILlmClientFactory>(_ => new StubLlmClientFactory());

        var configuration = new ConfigurationBuilder().Build();
        services.AddNodeExecutionContextFactory(configuration);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // 经 DI 解析工厂（生产装配路径），验证 eventBus 已注入。
        var factory = scope.ServiceProvider.GetRequiredService<NodeExecutionContextFactory>();

        var workflow = new Workflow { Name = "wf", Version = 1, IsActive = true };
        var execution = new ExecutionRecord { Id = executionId, Status = ExecutionStatus.Running };
        var node = new NodeDefinition { Id = nodeDefinitionId, TypeName = "stub", Name = "n" };
        var empty = new Dictionary<string, DataBatch>();

        var context = await factory.CreateAsync(
            workflow,
            execution,
            node,
            new StubNodeType(),
            empty,
            empty,
            empty,
            0,
            CancellationToken.None);

        // 通过工厂装配出的（已被审计装饰器包裹的）凭据访问器解析凭据。
        var resolved = await context.Credentials.GetCredentialAsync(credentialId, CancellationToken.None);

        Assert.NotNull(resolved);
        var evt = Assert.Single(eventBus.Published.OfType<CredentialAccessedEvent>());
        Assert.Equal(credentialId, evt.CredentialId);
        Assert.Equal(executionId, evt.ExecutionId);
        Assert.Equal(nodeDefinitionId, evt.NodeDefinitionId);
        Assert.Equal("Resolve", evt.AccessType);
    }
}
