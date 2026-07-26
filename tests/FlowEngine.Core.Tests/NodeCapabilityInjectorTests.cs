using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using Xunit;

namespace FlowEngine.Core.Tests;

/// <summary>
/// 验证 <see cref="NodeCapabilityInjector"/> 的能力解析优先级：上下文能力（如 <see cref="IExecutionLogger"/>）取自
/// <see cref="NodeExecutionContext"/>，DI 能力（自定义接口）取自 <see cref="IServiceProvider"/>；并对 Required / 可选缺失给出正确行为。
/// 使用极简假实现，不依赖 Moq（Core.Tests 未引用）。
/// </summary>
public sealed class NodeCapabilityInjectorTests
{
    private interface ITestCapability
    {
    }

    private sealed class TestCapability : ITestCapability
    {
    }

    /// <summary>仅声明 DI 来源能力（非上下文类型）。</summary>
    [NodeMeta(TypeName = "capDiNode", DisplayName = "CapDi", Category = NodeCategory.Test, Icon = "capDi")]
    private sealed class DiNode : NodeBase
    {
        [Inject] public ITestCapability? Capability { get; set; }

        public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
            => Task.FromResult(NodeHandlerOutput.Data(new DataBatch()));
    }

    /// <summary>仅依赖基类提供的上下文派生能力（Logger），用于验证其可由 NodeExecutionContext 解析。</summary>
    [NodeMeta(TypeName = "capContextNode", DisplayName = "CapContext", Category = NodeCategory.Test, Icon = "capContext")]
    private sealed class ContextNode : NodeBase
    {
        [Inject] public IExecutionLogger? Logger { get; private set; }

        public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
            => Task.FromResult(NodeHandlerOutput.Data(new DataBatch()));
    }

    /// <summary>声明 Required 的 DI 能力，缺失时应抛错。</summary>
    [NodeMeta(TypeName = "capRequiredNode", DisplayName = "CapRequired", Category = NodeCategory.Test, Icon = "capRequired")]
    private sealed class RequiredNode : NodeBase
    {
        [Inject(Required = true)] public ITestCapability? Capability { get; set; }

        public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
            => Task.FromResult(NodeHandlerOutput.Data(new DataBatch()));
    }

    /// <summary>声明可选 DI 能力，缺失时应留空且不抛错。</summary>
    [NodeMeta(TypeName = "capOptionalNode", DisplayName = "CapOptional", Category = NodeCategory.Test, Icon = "capOptional")]
    private sealed class OptionalNode : NodeBase
    {
        [Inject] public ITestCapability? Capability { get; set; }

        public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
            => Task.FromResult(NodeHandlerOutput.Data(new DataBatch()));
    }

    /// <summary>极简 <see cref="IServiceProvider"/>：仅返回预置的单一类型实例（未注册则返回 null）。</summary>
    private sealed class FakeServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object?> _services = new();

        public void Add(Type serviceType, object? instance) => _services[serviceType] = instance;

        public object? GetService(Type serviceType)
            => _services.TryGetValue(serviceType, out var value) ? value : null;
    }

    /// <summary>极简 <see cref="IExecutionLogger"/> 假实现（无副作用）。</summary>
    private sealed class FakeExecutionLogger : IExecutionLogger
    {
        public void LogInformation(string message, params object?[] args)
        {
        }

        public void LogWarning(string message, params object?[] args)
        {
        }

        public void LogError(Exception? exception, string message, params object?[] args)
        {
        }
    }

    [Fact]
    public void Inject_DiService_ResolvesFromServiceProvider()
    {
        var node = new DiNode();
        var services = new FakeServiceProvider();
        services.Add(typeof(ITestCapability), new TestCapability());

        NodeCapabilityInjector.Inject(node, services, new NodeExecutionContext());

        Assert.NotNull(node.Capability);
        Assert.IsType<TestCapability>(node.Capability);
    }

    [Fact]
    public void Inject_ContextCapability_ResolvesFromContext()
    {
        var node = new ContextNode();
        var context = new NodeExecutionContext { Logger = new FakeExecutionLogger() };

        // 即便不提供 DI 容器，上下文能力也应从 context 注入。
        NodeCapabilityInjector.Inject(node, null, context);

        Assert.NotNull(node.Logger);
        Assert.IsType<FakeExecutionLogger>(node.Logger);
    }

    [Fact]
    public void Inject_MissingRequired_Throws()
    {
        var node = new RequiredNode();
        var context = new NodeExecutionContext();

        var ex = Assert.Throws<NodeExecutionException>(
            () => NodeCapabilityInjector.Inject(node, null, context));

        Assert.Equal("CapabilityMissing", ex.ErrorCode);
    }

    [Fact]
    public void Inject_MissingOptional_LeavesNull()
    {
        var node = new OptionalNode();
        var context = new NodeExecutionContext();

        NodeCapabilityInjector.Inject(node, null, context);

        Assert.Null(node.Capability);
    }
}
