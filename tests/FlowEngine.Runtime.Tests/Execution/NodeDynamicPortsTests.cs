using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Registry;
using Xunit;

namespace FlowEngine.Runtime.Tests.Execution;

/// <summary>
/// 验证动态端口 <see cref="NodeBase.GetExtraPorts"/> 在 <see cref="ParameterHydrator"/> 之后、
/// 使用真实水合实例计算（计划 §A.4.1）：端口命名 case{i}（而非 SwitchCase.Name），并与基类端口叠加。
/// </summary>
public sealed class NodeDynamicPortsTests
{
    [Fact]
    public async Task SwitchNode_Hydrated_GetExtraPortsReflectsCases()
    {
        var cases = new List<SwitchCase>
        {
            new() { Name = "a", Label = "A", Value = "a" },
            new() { Name = "b", Label = "B", Value = "b" },
            new() { Name = "c", Label = "C", Value = "c" },
        };

        // 新实例（此时 Cases 为空，相当于 Activator.CreateInstance 的默认状态）。
        var node = new SwitchNode();
        var hydrator = new ParameterHydrator();
        await hydrator.HydrateAsync(node, new Dictionary<string, object> { ["cases"] = cases });

        // 基类 Ports 在构造时 Cases 为空，仅含 base ports（input + _default）；
        // 动态端口必须从“已水合的真实实例”的 GetExtraPorts 取得。
        var basePorts = ((INodeType)node).Ports;
        var getExtraPorts = typeof(SwitchNode)
            .GetMethod("GetExtraPorts", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var extra = (IReadOnlyList<PortDefinition>)getExtraPorts.Invoke(node, null)!;

        var effective = basePorts.Concat(extra).ToList();

        Assert.Contains(effective, p => p.Name == "case0");
        Assert.Contains(effective, p => p.Name == "case1");
        Assert.Contains(effective, p => p.Name == "case2");
        Assert.Contains(effective, p => p.Name == FlowConstants.PortNames.Default);
        Assert.DoesNotContain(effective, p => p.Name == "a"); // 端口用 case{i} 而非 SwitchCase.Name
    }
}
