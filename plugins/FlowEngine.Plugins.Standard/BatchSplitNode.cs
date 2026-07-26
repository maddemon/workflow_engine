using System.ComponentModel;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 分批处理节点（只分不收）。将输入集合按 <see cref="BatchSize"/> 切片，从 Loop 输出口逐批发出；
/// 每批经下游处理后由内核的反馈边回连本节点输入，节点凭借持久化上下文（<c>NodeContext</c>）记住迭代位置，
/// 直至全部批发出完毕才从 Done 输出口发出空批次。
/// <para>与 <see cref="LoopNode"/> 的区别：本节点只负责「切片下发」，不回收下游回流的「已处理窗口」
/// （<c>processedItems</c>）。回环激活时忽略传入输入、直接基于缓存的 <c>allItems</c> 推进位置，
/// Done 输出为固定空批次。窗口切分与位置推进复用 <see cref="BatchLoopHelper"/>，不重复循环逻辑。</para>
/// <para>状态保存在节点上下文：<c>allItems</c>（原始输入全集）、<c>position</c>（当前位置）。
/// <c>NodeContext</c> 由运行时跨调用保持：回环激活时内核复用旧上下文，新上游输入时内核清空重建。</para>
/// <para>拓扑约束：Loop 端口应仅有一条反馈边回连其输入，由 <c>WorkflowSchedulerKernel</c> 的通用反馈边机制驱动循环；
/// 节点自身不构建循环。</para>
/// </summary>
[NodeMeta(TypeName = "batchSplit", DisplayName = "Split In Batches", Category = NodeCategory.Flow, Icon = "split", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Loop, "Loop", PortDirection.Output, PortType.Main)]
[Port(FlowConstants.PortNames.Done, "Done", PortDirection.Output, PortType.Main)]
public sealed class BatchSplitNode : NodeBase
{
    /// <summary>
    /// 单批包含的最大项数。
    /// </summary>
    [Description("单批包含的最大项数。")]
    public int BatchSize { get; set; } = 1;

    /// <inheritdoc />
    /// <remarks>
    /// 分片语义：首次调用（或新上游输入导致内核清空上下文后）初始化上下文并存储原始输入全集；
    /// 之后每次回环激活基于 <c>position</c> 取当前窗口从 Loop 输出口发出，直到位置越过全集则从 Done 输出口发出空批次。
    /// 与 LoopNode 不同，本节点不累积下游回流输出——回环输入一律忽略，Done 输出为固定空批次。
    /// 节点上下文由运行时跨调用保持（见节点级持久化上下文方案），回环激活时内核复用旧上下文、非回边激活时内核清空重建。
    /// 窗口切分与位置推进已抽至 <see cref="BatchLoopHelper"/>，本节点仅负责「首调缓存全集 + 反馈忽略输入 + 取窗口」。
    /// </remarks>
    public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        BatchSize = Math.Max(1, BatchSize);

        // 首次调用（或新上游输入经内核清空上下文后）缓存原始输入全集 + position=0；
        // 回环激活时 EnsureInitialized 直接返回 false（上下文已存在），回环输入一律忽略——
        // 不重置 allItems、不回收下游回流输出，位置由持久化上下文推进。
        BatchLoopHelper.EnsureInitialized(NodeContext.State, input.InputBatch.Items);

        // Done 输出语义：batchSplit 不回收下游输出，固定发空批次（仅作为「全部分批完毕」信号）。
        var donePayload = new DataBatch { Items = [] };
        var result = BatchLoopHelper.EmitNextWindow(NodeContext.State, BatchSize, donePayload);

        // EmitNextWindow 返回带 BranchIndex（0=Loop, 1=Done）的结果；映射为命名端口输出，
        // 由 NodeBase.ToResult 回填等价 BranchIndex，保持与原 INodeType 路径一致的路由语义。
        var portName = result.BranchIndex == BatchLoopHelper.BranchDone
            ? FlowConstants.PortNames.Done
            : FlowConstants.PortNames.Loop;

        return Task.FromResult(NodeHandlerOutput.ToPort(portName, result.Output));
    }
}
