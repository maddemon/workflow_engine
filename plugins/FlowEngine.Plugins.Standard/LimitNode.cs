using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using System.ComponentModel;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 限制节点，限制数据项数量。
/// </summary>
[NodeMeta(TypeName = "limit", DisplayName = "Limit", Category = NodeCategory.Data, Icon = "hash", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class LimitNode : NodeBase
{
    /// <summary>
    /// 最大项目数。
    /// </summary>
    [Description("Maximum number of items to output.")]
    public int MaxItems { get; set; } = 10;

    /// <summary>
    /// 跳过的项目数。
    /// </summary>
    [Description("Number of items to skip from the beginning.")]
    public int Skip { get; set; } = 0;

    /// <inheritdoc />
    public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        var inputBatch = input.InputBatch;

        var items = inputBatch.Items
            .Skip(Skip)
            .Take(MaxItems)
            .ToList();

        return Task.FromResult(NodeHandlerOutput.Data(new DataBatch { Items = items }));
    }
}
