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
/// 回归测试：<see cref="NodeBase"/> 的直接执行路径（<c>INodeType.ExecuteAsync</c>）的能力注入。
/// 修复前 <see cref="NodeBase.InjectCapabilities"/> 每次执行都会 new ServiceCollection + BuildServiceProvider
/// （仅用于提供 INodeRegistry），存在规模执行下的重复建容器开销。修复后 INodeRegistry 改经
/// <see cref="NodeCapabilityInjector"/> 的上下文提供器从 <see cref="NodeExecutionContext.NodeRegistry"/> 解析，
/// 不再创建容器。本测试确保移除容器后，声明 <c>[Inject] INodeRegistry</c> 的节点在直接执行路径下仍能正确注入，
/// 且普通节点执行不受影响。
/// </summary>
public sealed class NodeBaseInjectCapabilitiesTests
{
    /// <summary>极简 <see cref="INodeRegistry"/> 假实现。</summary>
    private sealed class FakeNodeRegistry : INodeRegistry
    {
        public void Register(INodeType nodeType) => throw new NotImplementedException();
        public INodeType Get(string typeName) => throw new NotImplementedException();
        public bool TryGet(string typeName, out INodeType? nodeType) => throw new NotImplementedException();
        public IReadOnlyCollection<INodeType> GetAll() => throw new NotImplementedException();
        public INodeType CreateInstance(string typeName) => throw new NotImplementedException();
        public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors() => throw new NotImplementedException();
        public NodeTypeDescriptor GetDescriptor(string typeName) => throw new NotImplementedException();
    }

    /// <summary>插件同款：声明上下文派生的 INodeRegistry 能力。</summary>
    [NodeMeta(TypeName = "injRegistryNode", DisplayName = "InjRegistry", Category = NodeCategory.Test, Icon = "injRegistry")]
    private sealed class RegistryNode : NodeBase
    {
        [Inject] public INodeRegistry? Registry { get; private set; }

        public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
            => Task.FromResult(NodeHandlerOutput.Data(new DataBatch()));
    }

    /// <summary>普通节点，仅依赖上下文派生的 NodeExecutionContext 能力。</summary>
    [NodeMeta(TypeName = "injPlainNode", DisplayName = "InjPlain", Category = NodeCategory.Test, Icon = "injPlain")]
    private sealed class PlainNode : NodeBase
    {
        [Inject] public NodeExecutionContext Ctx { get; private set; } = null!;

        public bool Executed { get; private set; }

        public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
        {
            Executed = true;
            Assert.NotNull(Ctx);
            return Task.FromResult(NodeHandlerOutput.Data(new DataBatch()));
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithNodeRegistry_InjectsRegistryWithoutServiceProvider()
    {
        var node = new RegistryNode();
        var registry = new FakeNodeRegistry();
        var context = new NodeExecutionContext { NodeRegistry = registry };

        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        // 关键断言：移除每执行建容器后，[Inject] INodeRegistry 仍从 ctx.NodeRegistry 注入。
        Assert.Same(registry, node.Registry);
    }

    [Fact]
    public async Task ExecuteAsync_PlainNode_InjectsContextCapabilitiesAndExecutes()
    {
        var node = new PlainNode();
        var context = new NodeExecutionContext();

        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(node.Executed);
        Assert.NotNull(node.Ctx);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutNodeRegistry_RegistryIsNullAndExecutesSuccessfully()
    {
        var node = new RegistryNode();
        var context = new NodeExecutionContext { NodeRegistry = null };

        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(node.Registry);
    }
}
