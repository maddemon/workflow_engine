using System.Collections.Generic;
using System.ComponentModel;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 循环节点（迭代语义）。将输入集合按 <see cref="BatchSize"/> 分批，从 Loop 输出口逐批发出；
/// 每批经下游处理后回灌本节点输入，节点凭借持久化上下文（<c>NodeContext</c>）记住迭代位置，
/// 直至全部处理完才从 Done 输出口发出累积处理结果。
/// <para>状态保存在节点上下文：<c>allItems</c>（原始输入全集）、<c>position</c>（当前位置）、
/// <c>processedItems</c>（已处理累积，即下游回流的窗口）。回环输入为下游节点输出，节点忽略之、继续基于存储的 <c>allItems</c> 迭代。
/// <c>NodeContext</c> 由运行时跨调用保持：回环激活时内核复用旧上下文，新上游输入时内核清空重建。</para>
/// <para>拓扑约束：Loop 端口应仅有一条反馈边回连其输入。多路扇出后各自回连会导致 <c>position</c> 推进次数与
/// <c>processedItems</c> 累积顺序错乱，需配合聚合节点（WaitingArea 多输入端口）而非简单累加。</para>
/// <para>Done 输出语义：为下游回流的「已处理窗口」累积（<c>processedItems</c>），其数量/顺序取决于下游实际回灌，
/// 未必等于原始输入全集（如下游过滤/重排/部分失败）。</para>
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
    /// 单批包含的最大项数。
    /// </summary>
    [Description("单批包含的最大项数。")]
    public int BatchSize { get; set; } = 1;

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
    /// 迭代语义：首次调用（或新上游输入导致内核清空上下文后）初始化上下文并存储原始输入全集；
    /// 之后每次回环激活基于 <c>position</c> 取当前窗口从 Loop 输出口发出，并把回流的「已处理窗口」
    /// 累积进 <c>processedItems</c>，直到位置越过全集则从 Done 输出口发出累积结果。
    /// 节点上下文由运行时跨调用保持（见节点级持久化上下文方案），回环激活时内核复用旧上下文、非回边激活时内核清空重建。
    /// </remarks>
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken ct = default)
    {
        BatchSize = Math.Max(1, BatchSize);

        var nodeContext = context.NodeContext;

        // 首次调用（或新上游输入经内核清空上下文后）初始化迭代状态，缓存原始输入全集；
        // 回环激活时内核不会清空上下文（见 WorkflowSchedulerKernel），故 EnsureInitialized 仅在未初始化时重新初始化。
        // 回环输入为下游节点输出，不据此重置 allItems。「重置迭代」开关已废弃（恒定 true 曾在回环中导致死循环）。
        // 窗口切分与位置推进已抽至 BatchLoopHelper，Loop 与未来的 batchSplit 节点共用，避免逻辑重复。
        if (!BatchLoopHelper.EnsureInitialized(nodeContext, context.GetInputBatch().Items))
        {
            // 回环激活（下游处理完毕回流）：当前输入即下游的「已处理窗口」P，累积进 processedItems。
            // 位置与 allItems 由持久化上下文保持，不重置。
            var processed = nodeContext.Get<List<DataItem>>(BatchLoopHelper.KeyProcessedItems) ?? [];
            processed.AddRange(context.GetInputBatch().Items);
            nodeContext[BatchLoopHelper.KeyProcessedItems] = processed;
        }

        // Done 输出语义：为下游回流的「已处理窗口」累积（processedItems）；首次调用时为空批次。
        var donePayload = new DataBatch { Items = nodeContext.Get<List<DataItem>>(BatchLoopHelper.KeyProcessedItems) ?? [] };
        return Task.FromResult(BatchLoopHelper.EmitNextWindow(nodeContext, BatchSize, donePayload));
    }
}
