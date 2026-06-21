using System.Collections.Concurrent;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.WaitingArea;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 封装执行循环中的全部可变状态，消除方法间长参数列表。
/// 在 <see cref="WorkflowExecutor.ExecuteLoopAsync"/> 入口构造，传递给各内部方法。
/// </summary>
public sealed class ExecutionSession
{
    public Workflow Workflow { get; }
    public ExecutionRecord Execution { get; set; }
    public Guid ExecutionRecordId { get; }

    public Dictionary<Guid, NodeDefinition> NodeMap { get; }
    public ILookup<(Guid SourceNodeId, string SourcePortName), Connection> ConnectionsBySource { get; }

    public ExecutionQueue Queue { get; }
    public WaitingArea.WaitingArea WaitingArea { get; }
    public ExecutionStateMachine StateMachine { get; }

    public ConcurrentDictionary<string, DataBatch> SuccessfulOutputs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, DataBatch> LatestBatches { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<Guid, ILlmClient> NodeLlmClients { get; } = new();

    public FlowEngineDbContext DbContext { get; }

    public ExecutionSession(
        Workflow workflow,
        ExecutionRecord execution,
        Guid executionRecordId,
        FlowEngineDbContext dbContext)
    {
        Workflow = workflow;
        Execution = execution;
        ExecutionRecordId = executionRecordId;
        DbContext = dbContext;

        NodeMap = workflow.Nodes.ToDictionary(n => n.Id);
        ConnectionsBySource = workflow.Connections
            .ToLookup(c => (c.SourceNodeId, c.SourcePortName));

        Queue = new ExecutionQueue();
        WaitingArea = new WaitingArea.WaitingArea();
        StateMachine = new ExecutionStateMachine(ExecutionStatus.Pending);
    }
}
