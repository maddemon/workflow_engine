using System.ComponentModel;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 循环节点（单窗口语义）。将输入集合的前 <see cref="BatchSize"/> 项从 Loop 输出口发出单个窗口；
/// 当输入为空时直接走 Done 输出口（空批）。
/// <para>限制：本节点仅发送单个窗口（前 BatchSize 项），超出部分不会被迭代处理。请勿将 Loop 输出口回连本节点输入，否则内核重跑仍只发首批，可能造成死循环或数据重复。</para>
/// </summary>
public sealed class LoopNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "loop";

    /// <inheritdoc />
    public string DisplayName => "Loop Over Items";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "repeat";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 单窗口包含的最大项数（仅发送前 BatchSize 项，超出部分不迭代处理）。
    /// </summary>
    [Description("单窗口包含的最大项数（仅发送前 BatchSize 项，超出部分不迭代处理）。")]
    public int BatchSize { get; set; } = 1;

    /// <summary>
    /// 是否重置索引。
    /// </summary>
    [Description("Whether to reset the item index at the start of each batch.")]
    public bool ResetIndex { get; set; } = false;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Loop, DisplayName = "Loop", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Done, DisplayName = "Done", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    /// <remarks>
    /// 单窗口语义：输入为空时走 Done 输出口（BranchIndex = 1）并返回空批；
    /// 否则取前 min(BatchSize, 总项数) 条，从 Loop 输出口（BranchIndex = 0）发出单个窗口。
    /// 执行内核不会回灌 nextBatch/position 等迭代参数，因此不会迭代，也不存在死循环路径。
    /// </remarks>
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        BatchSize = Math.Max(1, BatchSize);

        var inputBatch = context.GetInputBatch();
        var allItems = inputBatch.Items;

        // 输入为空：直接走 Done 输出口，返回空批。
        if (allItems.Count == 0)
        {
            return Task.FromResult(new NodeExecutionResult
            {
                Success = true,
                Output = new DataBatch(),
                BranchIndex = 1 // done
            });
        }

        // 单窗口：仅取前 min(BatchSize, 总项数) 条，从 Loop 输出口发出。
        var windowItems = allItems.Take(BatchSize).ToList();

        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = windowItems },
            BranchIndex = 0 // loop
        });
    }
}
