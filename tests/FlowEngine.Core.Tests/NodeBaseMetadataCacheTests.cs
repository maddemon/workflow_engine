using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Tests;

/// <summary>
/// NodeBase 构造函数的元数据应经静态缓存按 Type 读取，消除每次克隆实例时的反射开销。
/// 对应设计文档 2026-07-26-nodetype-execution-instance-separation.md §4.1.A。
/// </summary>
[NodeMeta(TypeName = "test.cache.node", DisplayName = "缓存测试节点", Category = NodeCategory.Test, Icon = "test-icon")]
[Port("in", "输入", PortDirection.Input)]
[Port("out", "输出", PortDirection.Output)]
public sealed class CacheTestNode : NodeBase
{
    public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
        => Task.FromResult(NodeHandlerOutput.Data(new DataBatch()));
}

public class NodeBaseMetadataCacheTests
{
    [Fact]
    public void Constructor_SingleInstance_ExposesDeclaredMetadata()
    {
        var node = new CacheTestNode();
        INodeType asType = node;

        Assert.Equal("test.cache.node", asType.TypeName);
        Assert.Equal("缓存测试节点", asType.DisplayName);
        Assert.Equal(NodeCategory.Test.ToString(), asType.Category);
        Assert.Equal("test-icon", asType.Icon);
        Assert.False(asType.DefaultIsEntry);
    }

    [Fact]
    public void Constructor_SingleInstance_ExposesAllDeclaredPorts()
    {
        var node = new CacheTestNode();
        INodeType asType = node;

        Assert.Equal(2, asType.Ports.Count);
        Assert.Contains(asType.Ports, p => p.Name == "in" && p.Direction == PortDirection.Input);
        Assert.Contains(asType.Ports, p => p.Name == "out" && p.Direction == PortDirection.Output);
    }

    [Fact]
    public void Constructor_ManyInstances_DoNotThrow_And_MetadataConsistent()
    {
        const int count = 100;
        var nodes = new INodeType[count];
        for (var i = 0; i < count; i++)
        {
            nodes[i] = new CacheTestNode();
        }

        foreach (var node in nodes)
        {
            Assert.Equal("test.cache.node", node.TypeName);
            Assert.Equal(2, node.Ports.Count);
        }
    }

    [Fact]
    public void Constructor_ManyInstances_PortsArraySharedViaCache()
    {
        // 缓存读取的可观测信号：多次构造同类型实例时，各实例的端口集合应为同一缓存数组引用，
        // 而非每次重新反射生成新列表。该断言在“每次反射”实现下会失败（各实例持有独立列表）。
        var first = (INodeType)new CacheTestNode();
        var second = (INodeType)new CacheTestNode();

        Assert.Same(first.Ports, second.Ports);
    }

    [Fact]
    public void Constructor_DistinctTypes_ProduceDistinctCachedMetadata()
    {
        var node = new CacheTestNode();
        var other = new CacheTestNode2();

        INodeType a = node;
        INodeType b = other;

        Assert.Equal("test.cache.node", a.TypeName);
        Assert.Equal("test.cache.node2", b.TypeName);
        Assert.NotSame(a.Ports, b.Ports);
    }
}

[NodeMeta(TypeName = "test.cache.node2", DisplayName = "缓存测试节点二", Category = NodeCategory.Test, Icon = "test-icon-2")]
[Port("in", "输入", PortDirection.Input)]
public sealed class CacheTestNode2 : NodeBase
{
    public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
        => Task.FromResult(NodeHandlerOutput.Data(new DataBatch()));
}
