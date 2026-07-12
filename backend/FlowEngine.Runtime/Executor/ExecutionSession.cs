using System.Collections.Concurrent;
using System.Text.Json.Nodes;
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
    /// 凭据访问器覆写。为 null 时使用节点执行上下文工厂的默认访问器（普通执行）；
    /// Dry-Run 注入临时凭据访问器。
    /// </summary>
    public ICredentialAccessor? CredentialAccessor { get; init; }

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
    }
}
