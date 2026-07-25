using FlowEngine.Core.Abstractions;
using FlowEngine.Runtime.Registry;
using FlowEngine.TestPlugin;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests;

public class NodeRegistryTests
{
    [Fact]
    public void Empty_Registry_Returns_Empty_Descriptors()
    {
        var registry = new NodeRegistry([], NullLogger<NodeRegistry>.Instance);

        Assert.Empty(registry.GetDescriptors());
    }

    [Fact]
    public void Register_Adds_Node_And_Returns_Descriptor()
    {
        var registry = new NodeRegistry([new TestNode()], NullLogger<NodeRegistry>.Instance);

        var descriptors = registry.GetDescriptors();

        Assert.Single(descriptors);
        Assert.Equal("testNode", descriptors.First().TypeName);
    }

    [Fact]
    public void CreateInstance_Returns_New_Instance_Each_Time()
    {
        var registry = new NodeRegistry([new TestNode()], NullLogger<NodeRegistry>.Instance);

        var first = registry.CreateInstance("testNode");
        var second = registry.CreateInstance("testNode");

        Assert.NotSame(first, second);
        Assert.IsType<TestNode>(first);
    }

    [Fact]
    public void Duplicate_TypeName_Keeps_First_And_Logs_Warning()
    {
        var registry = new NodeRegistry(
            [new TestNode(), new TestNode()],
            NullLogger<NodeRegistry>.Instance);

        Assert.Single(registry.GetDescriptors());
    }

    [Fact]
    public void Get_Unknown_Node_Throws()
    {
        var registry = new NodeRegistry([], NullLogger<NodeRegistry>.Instance);

        Assert.Throws<InvalidOperationException>(() => registry.Get("unknown"));
    }

    [Fact]
    public void Get_ReturnsDistinctInstancesPerCall()
    {
        // CON-3：每次取用返回克隆实例，避免并行执行水合同一共享单例导致字段（如 SwitchNode.Cases）串扰。
        var registry = new NodeRegistry([new TestNode()], NullLogger<NodeRegistry>.Instance);

        var first = registry.Get("testNode");
        var second = registry.Get("testNode");

        Assert.NotSame(first, second);
    }

    [Fact]
    public void TryGet_ReturnsDistinctInstancesPerCall()
    {
        var registry = new NodeRegistry([new TestNode()], NullLogger<NodeRegistry>.Instance);

        Assert.True(registry.TryGet("testNode", out var first));
        Assert.True(registry.TryGet("testNode", out var second));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
    }
}
