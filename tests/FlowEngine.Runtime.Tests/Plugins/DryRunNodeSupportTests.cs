using FlowEngine.Core.Abstractions;
using FlowEngine.Plugins.Standard;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// 验证纯计算节点实现 ISupportsDryRun，副作用节点不实现。
/// </summary>
public class DryRunNodeSupportTests
{
    [Theory]
    [InlineData(typeof(SetNode))]
    [InlineData(typeof(MergeNode))]
    [InlineData(typeof(IfNode))]
    [InlineData(typeof(SwitchNode))]
    [InlineData(typeof(CalculatorToolNode))]
    [InlineData(typeof(FilterNode))]
    [InlineData(typeof(SortNode))]
    [InlineData(typeof(LimitNode))]
    [InlineData(typeof(AggregateNode))]
    public void PureComputationNodes_Implement_ISupportsDryRun(Type nodeType)
    {
        Assert.True(typeof(ISupportsDryRun).IsAssignableFrom(nodeType));
    }

    [Theory]
    [InlineData(typeof(HttpRequestNode))]
    [InlineData(typeof(HttpToolNode))]
    [InlineData(typeof(LlmNode))]
    [InlineData(typeof(ShellToolNode))]
    public void SideEffectNodes_DoNotImplement_ISupportsDryRun(Type nodeType)
    {
        Assert.False(typeof(ISupportsDryRun).IsAssignableFrom(nodeType));
    }
}
