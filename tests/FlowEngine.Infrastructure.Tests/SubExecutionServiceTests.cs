using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Agent;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Http;
using FlowEngine.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowEngine.Infrastructure.Tests;

/// <summary>
/// 验证 <see cref="SubExecutionService.ExecuteSubAsync"/> 的“每运行实例隔离”：
/// 不得复用调用方传入的 nodeType，必须通过 <see cref="INodeRegistry.CreateInstance"/> 取得全新实例，
/// 并对新实例绑定执行期服务（避免真实并发下的共享单例竞争）。
/// </summary>
public sealed class SubExecutionServiceTests
{
    [Fact]
    public async Task ExecuteSubAsync_UsesDistinctInstance_NotCallerInstance()
    {
        var factory = new FakeExecutionContextFactory();
        var registry = new FakeNodeRegistry();
        var services = new FakeServiceProvider();
        var sut = new SubExecutionService(factory, registry, services);

        // 调用方传入的实例（另一个 NodeBase 子类），其 ExecuteAsync 不应被调用。
        var callerNode = new CallerNode();
        var inputs = new Dictionary<string, DataBatch>();

        await sut.ExecuteSubAsync(new Workflow(), new ExecutionRecord(), new NodeDefinition(), callerNode, inputs, 0);

        // 调用方实例未被复用执行。
        Assert.False(callerNode.WasExecuted);

        // 实际执行的是 CreateInstance 返回的独立 SpyNode 实例。
        var executedInstance = Assert.Single(factory.CapturedNodeInstances);
        var spy = Assert.IsType<SpyNode>(executedInstance);
        Assert.True(spy.WasExecuted);
    }

    [Fact]
    public async Task ExecuteSubAsync_BindsServicesToNewInstance()
    {
        var factory = new FakeExecutionContextFactory();
        var registry = new FakeNodeRegistry();
        var services = new FakeServiceProvider();
        // 测试中主动提供非 null 的 fake，验证 BindServices 会把可解析服务透传注入新实例。
        services.Add(typeof(IHttpExecutionService), new FakeHttpExecutionService());
        services.Add(typeof(IToolResolver), new FakeToolResolver());

        var sut = new SubExecutionService(factory, registry, services);
        // SubExecutionService 自身即以 ISubExecutionService 身份经 serviceProvider 注入新实例（生产中由 DI 容器注册）。
        services.Add(typeof(ISubExecutionService), sut);
        var callerNode = new CallerNode();
        await sut.ExecuteSubAsync(
            new Workflow(),
            new ExecutionRecord(),
            new NodeDefinition(),
            callerNode,
            new Dictionary<string, DataBatch>(),
            0);

        var spy = Assert.IsType<SpyNode>(Assert.Single(factory.CapturedNodeInstances));
        Assert.NotNull(spy.RecordedHttp);
        Assert.NotNull(spy.RecordedSub);
        Assert.NotNull(spy.RecordedTools);
        // sub 服务应透传 this（SubExecutionService 自身）。
        Assert.Same(sut, spy.RecordedSub);
    }

    [Fact]
    public async Task ExecuteSubAsync_ParallelCalls_UseIndependentInstances()
    {
        var factory = new FakeExecutionContextFactory();
        var registry = new FakeNodeRegistry();
        var services = new FakeServiceProvider();
        var sut = new SubExecutionService(factory, registry, services);

        var callerNode = new CallerNode();

        // 并行两次子执行，各自不同 runIndex / inputs。
        var taskA = sut.ExecuteSubAsync(
            new Workflow(), new ExecutionRecord(), new NodeDefinition(), callerNode,
            new Dictionary<string, DataBatch>(), 1);
        var taskB = sut.ExecuteSubAsync(
            new Workflow(), new ExecutionRecord(), new NodeDefinition(), callerNode,
            new Dictionary<string, DataBatch>(), 2);
        await Task.WhenAll(taskA, taskB);

        // 两次执行各使用独立的 SpyNode 实例，互不串改。
        Assert.Equal(2, factory.CapturedNodeInstances.Count);
        var instA = factory.CapturedNodeInstances[0];
        var instB = factory.CapturedNodeInstances[1];
        Assert.NotSame(instA, instB);
        Assert.IsType<SpyNode>(instA);
        Assert.IsType<SpyNode>(instB);
        Assert.True(((SpyNode)instA).WasExecuted);
        Assert.True(((SpyNode)instB).WasExecuted);
    }

    [Fact]
    public async Task ExecuteSubAsync_ParallelCalls_InstancesDistinctFromCaller()
    {
        var factory = new FakeExecutionContextFactory();
        var registry = new FakeNodeRegistry();
        var services = new FakeServiceProvider();
        var sut = new SubExecutionService(factory, registry, services);

        // 调用方传入的实例（另一个 NodeBase 子类），其 ExecuteAsync 不应被复用。
        var callerNode = new CallerNode();

        // 并行两次子执行，传入 SAME callerNode。
        var taskA = sut.ExecuteSubAsync(
            new Workflow(), new ExecutionRecord(), new NodeDefinition(), callerNode,
            new Dictionary<string, DataBatch>(), 1);
        var taskB = sut.ExecuteSubAsync(
            new Workflow(), new ExecutionRecord(), new NodeDefinition(), callerNode,
            new Dictionary<string, DataBatch>(), 2);
        await Task.WhenAll(taskA, taskB);

        // 两次执行各捕获一个独立 SpyNode 实例。
        Assert.Equal(2, factory.CapturedNodeInstances.Count);
        var capturedA = factory.CapturedNodeInstances[0];
        var capturedB = factory.CapturedNodeInstances[1];

        // 两个子运行相互独立。
        Assert.NotSame(capturedA, capturedB);

        // 两者均不是调用方实例 —— 真实并发陷阱已被消除。
        Assert.NotSame(capturedA, callerNode);
        Assert.NotSame(capturedB, callerNode);

        // 调用方实例从未被执行。
        Assert.False(callerNode.WasExecuted);

        // 两个被捕获实例均为独立执行的 SpyNode。
        var spyA = Assert.IsType<SpyNode>(capturedA);
        var spyB = Assert.IsType<SpyNode>(capturedB);
        Assert.True(spyA.WasExecuted);
        Assert.True(spyB.WasExecuted);
    }
}

/// <summary>调用方传入的节点类型（另一个 NodeBase 子类），用于证明它不会被复用执行。</summary>
[NodeMeta(TypeName = "CallerNode", DisplayName = "Caller", Category = NodeCategory.Test, Icon = "caller")]
public sealed class CallerNode : NodeBase
{
    /// <summary>ExecuteAsync 是否被调用（应为 false，证明未被复用）。</summary>
    public bool WasExecuted { get; private set; }

    /// <inheritdoc />
    public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        WasExecuted = true;
        return Task.FromResult(NodeHandlerOutput.Data(new DataBatch()));
    }
}

/// <summary>由 <see cref="FakeNodeRegistry.CreateInstance"/> 返回的独立实例；记录自身被执行的实例身份与注入的服务。</summary>
[NodeMeta(TypeName = "SpyNode", DisplayName = "Spy", Category = NodeCategory.Test, Icon = "spy")]
public sealed class SpyNode : NodeBase
{
    /// <summary>经 <see cref="NodeCapabilityInjector"/> 注入的 HTTP 执行服务。</summary>
    [Inject] public IHttpExecutionService? Http { get; private set; }

    /// <summary>经 <see cref="NodeCapabilityInjector"/> 注入的子执行服务。</summary>
    [Inject] public ISubExecutionService? Sub { get; private set; }

    /// <summary>经 <see cref="NodeCapabilityInjector"/> 注入的工具解析器。</summary>
    [Inject] public IToolResolver? ToolResolver { get; private set; }

    /// <summary>ExecuteAsync 是否被调用。</summary>
    public bool WasExecuted { get; private set; }

    /// <summary>执行期注入的 HTTP 执行服务（来自 NodeCapabilityInjector）。</summary>
    public IHttpExecutionService? RecordedHttp { get; private set; }

    /// <summary>执行期注入的子执行服务（来自 NodeCapabilityInjector）。</summary>
    public ISubExecutionService? RecordedSub { get; private set; }

    /// <summary>执行期注入的工具解析器（来自 NodeCapabilityInjector）。</summary>
    public IToolResolver? RecordedTools { get; private set; }

    /// <inheritdoc />
    public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        WasExecuted = true;
        RecordedHttp = Http;
        RecordedSub = Sub;
        RecordedTools = ToolResolver;
        return Task.FromResult(NodeHandlerOutput.Data(new DataBatch()));
    }
}

/// <summary>fake <see cref="INodeRegistry"/>：仅实现 <see cref="CreateInstance"/>（返回独立 SpyNode），其余抛 NotImplementedException。</summary>
public sealed class FakeNodeRegistry : INodeRegistry
{
    /// <inheritdoc />
    public INodeType CreateInstance(string typeName) => new SpyNode();

    /// <inheritdoc />
    public INodeType Get(string typeName) => throw new System.NotImplementedException();

    /// <inheritdoc />
    public bool TryGet(string typeName, out INodeType? nodeType) => throw new System.NotImplementedException();

    /// <inheritdoc />
    public IReadOnlyCollection<INodeType> GetAll() => throw new System.NotImplementedException();

    /// <inheritdoc />
    public void Register(INodeType nodeType) => throw new System.NotImplementedException();

    /// <inheritdoc />
    public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors() => throw new System.NotImplementedException();

    /// <inheritdoc />
    public NodeTypeDescriptor GetDescriptor(string typeName) => throw new System.NotImplementedException();
}

/// <summary>fake <see cref="INodeExecutionContextFactory"/>：记录传入的 nodeInstance 并直接构造 <see cref="NodeExecutionContext"/>。</summary>
public sealed class FakeExecutionContextFactory : INodeExecutionContextFactory
{
    /// <summary>执行中捕获到的 nodeInstance（即 SubExecutionService 传入的新运行实例）。</summary>
    public List<INodeType> CapturedNodeInstances { get; } = new();

    /// <inheritdoc />
    public Task<NodeExecutionContext> CreateAsync(
        Workflow workflow,
        ExecutionRecord execution,
        NodeDefinition node,
        INodeType nodeInstance,
        IReadOnlyDictionary<string, DataBatch> inputs,
        IReadOnlyDictionary<string, DataBatch> successfulOutputs,
        IReadOnlyDictionary<string, DataBatch> latestBatches,
        int runIndex,
        CancellationToken cancellationToken,
        ICredentialAccessor? credentialAccessorOverride = null,
        IReadOnlyDictionary<string, object?>? extraGlobals = null,
        IDictionary<string, object?>? nodeContext = null)
    {
        CapturedNodeInstances.Add(nodeInstance);
        return Task.FromResult(new NodeExecutionContext { Node = node });
    }
}

/// <summary>fake <see cref="IServiceProvider"/>：按类型返回预置的服务（未注册则返回 null，与 DI 行为一致）。</summary>
public sealed class FakeServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object?> _services = new();

    /// <summary>注册一个服务实例。</summary>
    /// <param name="serviceType">服务类型。</param>
    /// <param name="instance">服务实例（可为 null）。</param>
    public void Add(Type serviceType, object? instance) => _services[serviceType] = instance;

    /// <inheritdoc />
    public object? GetService(Type serviceType)
        => _services.TryGetValue(serviceType, out var value) ? value : null;
}

/// <summary>fake <see cref="IHttpExecutionService"/>（测试中仅用于透传验证，不实际发起请求）。</summary>
public sealed class FakeHttpExecutionService : IHttpExecutionService
{
    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(HttpExecutionRequest request, NodeExecutionContext context, CancellationToken ct = default)
        => Task.FromResult(new NodeExecutionResult { Success = true });
}

/// <summary>fake <see cref="IToolResolver"/>（测试中仅用于透传验证，不实际解析）。</summary>
public sealed class FakeToolResolver : IToolResolver
{
    /// <inheritdoc />
    public ToolResolution Resolve(LlmToolCall toolCall)
        => new(null, null, null, null);
}
