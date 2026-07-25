using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.WaitingArea;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 封装执行循环中的全部可变状态，消除方法间长参数列表。
/// 在 <see cref="WorkflowSchedulerKernel.RunAsync"/> 入口构造，传递给各内部方法。
/// 纯内存、不持有 <c>DbContext</c>（持久化由外壳经 <see cref="IExecutionSideEffects"/> 完成）。
/// </summary>
public sealed class ExecutionSession
{
    /// <summary>
    /// 空敏感值集合：普通执行使用，仅对 <see cref="FlowEngine.Core.Data.CredentialValue"/> 脱敏，字面量不额外脱敏。
    /// </summary>
    public static readonly IReadOnlySet<string> EmptySensitiveValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public Workflow Workflow { get; }
    public ExecutionRecord Execution { get; private set; }
    public Guid ExecutionRecordId { get; }

    public Dictionary<string, NodeDefinition> NodeMap { get; }
    public ILookup<(string SourceNodeId, string SourcePortName), Connection> ConnectionsBySource { get; }

    public ExecutionQueue Queue { get; }
    public WaitingArea.WaitingArea WaitingArea { get; }
    public ExecutionStateMachine StateMachine { get; }

    public ConcurrentDictionary<string, DataBatch> SuccessfulOutputs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, DataBatch> LatestBatches { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, ILlmClient> NodeLlmClients { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, JsonNode?> Memory { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 节点级持久化上下文：键为节点实例 ID，值为该节点跨调用保持的状态字典。
    /// 由 <see cref="WorkflowSchedulerKernel"/> 在每次处理节点时获取/复用。
    /// </summary>
    public ConcurrentDictionary<string, IDictionary<string, object?>> NodeContexts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 单节点反馈边激活累计计数（用于环路失控保护，见 <see cref="EngineDefaultsOptions.MaxCycleIterations"/>）。
    /// 键为节点实例 ID；非回边激活（新上游输入）时由内核清零，开启新一轮循环。
    /// </summary>
    public ConcurrentDictionary<string, long> FeedbackActivationCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 环路回边集合（连接四元组）。由 <see cref="CycleDetector"/> 在构造时基于连接图计算一次，
    /// 供 <see cref="WorkflowSchedulerKernel"/> 区分「环路回边激活」与「新上游输入激活」，
    /// 从而决定是否重置节点上下文（见节点级持久化上下文方案 Task 9）。
    /// </summary>
    public IReadOnlySet<(string SourceNodeId, string? SourcePortName, string TargetNodeId, string? TargetPortName)> FeedbackEdgeKeys { get; init; }

    /// <summary>
    /// 凭据访问器覆写。为 null 时使用节点执行上下文工厂的默认访问器（普通执行）；
    /// Dry-Run 注入临时凭据访问器。
    /// </summary>
    public ICredentialAccessor? CredentialAccessor { get; init; }

    /// <summary>
    /// 调度唤醒信号（CON-6）。内核空闲（队列空且等待区非空）时阻塞于该信号，
    /// 任一工作项入队后由 <see cref="PulseScheduler"/> 释放，实现事件驱动唤醒，
    /// 取代固定 500ms 空轮询。每个执行独立持有，避免并发执行互相唤醒。
    /// </summary>
    public SemaphoreSlim SchedulerWake { get; } = new SemaphoreSlim(0, 1);

    /// <summary>
    /// 脉冲调度唤醒信号（CON-6）。内部按类型队列 <see cref="ExecutionQueue.EnqueueAsync"/> 后调用，
    /// 仅当当前无待消费信号时释放，确保计数不超过 1，避免无界增长。
    /// </summary>
    public void PulseScheduler()
    {
        if (SchedulerWake.CurrentCount == 0)
        {
            SchedulerWake.Release();
        }
    }

    /// <summary>
    /// 字面量敏感值集合（用于脱敏输出/参数中的字面凭据值）。
    /// 普通执行为空集合；Dry-Run 为临时凭据的字面字段值集合。
    /// </summary>
    public IReadOnlySet<string> SensitiveValues { get; init; } = EmptySensitiveValues;

    public ExecutionSession(
        Workflow workflow,
        ExecutionRecord execution,
        Guid executionRecordId,
        INodeRegistry? nodeRegistry = null)
    {
        Workflow = workflow;
        Execution = execution;
        ExecutionRecordId = executionRecordId;

        NodeMap = workflow.Nodes.ToDictionary(n => n.Id);
        ConnectionsBySource = workflow.Connections
            .ToLookup(c =>
            {
                var portName = c.SourcePortName;
                if (string.IsNullOrEmpty(portName) && NodeMap.TryGetValue(c.SourceNodeId, out var srcNode))
                {
                    // 优先使用节点显式声明的 Ports
                    portName = srcNode.Ports
                        .FirstOrDefault(p => p.Direction == PortDirection.Output)?.Name ?? string.Empty;

                    // 容错兜底：使用注册中心的 PortDefinition 作为权威数据源
                    if (string.IsNullOrEmpty(portName) && nodeRegistry is not null)
                    {
                        try
                        {
                            var descriptor = nodeRegistry.GetDescriptor(srcNode.TypeName);
                            portName = descriptor.Ports
                                .FirstOrDefault(p => p.Direction == PortDirection.Output)?.Name ?? string.Empty;
                        }
                        catch
                        {
                            // Registry 查找失败，保持空字符串
                        }
                    }
                }
                return (c.SourceNodeId, (portName ?? string.Empty).ToLowerInvariant());
            });

        Queue = new ExecutionQueue();
        WaitingArea = new WaitingArea.WaitingArea();
        StateMachine = new ExecutionStateMachine(ExecutionStatus.Pending);

        FeedbackEdgeKeys = CycleDetector.ComputeBackEdges(workflow.Connections);
    }
}
