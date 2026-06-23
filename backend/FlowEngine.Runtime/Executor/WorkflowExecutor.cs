using System.Collections;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.WaitingArea;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Executor;

public sealed class WorkflowExecutor : IEngine
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly INodeRegistry _nodeRegistry;
    private readonly NodeExecutionContextFactory _contextFactory;
    private readonly ErrorStrategyHandler _errorHandler;
    private readonly WorkflowExecutionQueue _executionQueue;
    private readonly ILogger<WorkflowExecutor> _logger;

    public WorkflowExecutor(
        FlowEngineDbContext dbContext,
        INodeRegistry nodeRegistry,
        NodeExecutionContextFactory contextFactory,
        ErrorStrategyHandler errorHandler,
        WorkflowExecutionQueue executionQueue,
        ILogger<WorkflowExecutor> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _nodeRegistry = nodeRegistry ?? throw new ArgumentNullException(nameof(nodeRegistry));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        _executionQueue = executionQueue ?? throw new ArgumentNullException(nameof(executionQueue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ExecutionId> StartAsync(
        Guid workflowDefinitionId,
        object? triggerPayload = null,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _dbContext.Workflows
            .FirstOrDefaultAsync(w => w.Id == workflowDefinitionId, cancellationToken)
            .ConfigureAwait(false);

        if (workflow is null)
        {
            throw new InvalidOperationException($"工作流 '{workflowDefinitionId}' 不存在。");
        }

        var executionRecord = new ExecutionRecord
        {
            WorkflowDefinitionId = workflowDefinitionId,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Pending,
            NodeRecords = []
        };

        _dbContext.ExecutionRecords.Add(executionRecord);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _executionQueue.EnqueueAsync(
            new WorkflowExecutionWorkItem(executionRecord.Id, workflowDefinitionId, triggerPayload),
            cancellationToken).ConfigureAwait(false);

        return ExecutionId.From(executionRecord.Id);
    }

    public async Task ExecuteLoopAsync(
        Workflow workflow,
        Guid executionRecordId,
        object? triggerPayload,
        FlowEngineDbContext executionStore,
        CancellationToken cancellationToken)
    {
        var execution = await executionStore.ExecutionRecords
            .FirstOrDefaultAsync(e => e.Id == executionRecordId, cancellationToken)
            .ConfigureAwait(false);
        if (execution is null) return;

        var session = new ExecutionSession(workflow, execution, executionRecordId, executionStore);
        session.StateMachine.Start();
        session.Execution.Status = ExecutionStatus.Running;
        await executionStore.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await EnqueueEntryNodesAsync(session, triggerPayload, cancellationToken).ConfigureAwait(false);

        const int IdleDelayMilliseconds = 500;

        while (!cancellationToken.IsCancellationRequested)
        {
            await ProcessTimeoutsAsync(session, cancellationToken).ConfigureAwait(false);

            if (session.Queue.Reader.TryRead(out var item))
            {
                var shouldStop = await ProcessNodeAsync(item, session, cancellationToken).ConfigureAwait(false);

                if (shouldStop)
                {
                    session.StateMachine.Fail();
                    break;
                }

                continue;
            }

            if (session.WaitingArea.IsEmpty)
            {
                break;
            }

            await Task.Delay(IdleDelayMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            session.StateMachine.Cancel();
            session.WaitingArea.CleanupExecution(execution.Id);
        }
        else if (session.StateMachine.Status == ExecutionStatus.Running)
        {
            session.StateMachine.Complete();
        }

        session.Execution.Status = session.StateMachine.Status;
        await executionStore.SaveChangesAsync(default).ConfigureAwait(false);
    }

    private async Task EnqueueEntryNodesAsync(
        ExecutionSession session,
        object? triggerPayload,
        CancellationToken cancellationToken)
    {
        var triggerBatch = CreateDataBatch(triggerPayload);
        var hasInputConnections = session.Workflow.Connections
            .Select(c => c.TargetNodeId)
            .ToHashSet();

        foreach (var node in session.Workflow.Nodes)
        {
            var nodeType = _nodeRegistry.Get(node.TypeName);
            var isExplicitEntry = node.IsEntry || nodeType.DefaultIsEntry;
            var isImplicitEntry = !hasInputConnections.Contains(node.Id);

            if (!isExplicitEntry && !isImplicitEntry)
            {
                continue;
            }

            var inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);
            var inputPorts = GetInputPortNames(nodeType);
            if (inputPorts.Count > 0)
            {
                inputs[inputPorts[0]] = triggerBatch;
            }

            await session.Queue.EnqueueAsync(
                new NodeWorkItem(session.Execution.Id, node.Id, inputs),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> ProcessNodeAsync(
        NodeWorkItem item,
        ExecutionSession session,
        CancellationToken cancellationToken)
    {
        if (!session.NodeMap.TryGetValue(item.NodeInstanceId, out var node))
        {
            return false;
        }

        var nodeType = _nodeRegistry.Get(node.TypeName);
        var executionMode = nodeType.ExecutionMode;
        var runCount = executionMode == ExecutionMode.OncePerItem
            ? Math.Max(1, item.Inputs.Values.Max(b => b.Items.Count))
            : 1;

        NodeExecutionResult? finalResult = null;

        for (var runIndex = 0; runIndex < runCount; runIndex++)
        {
            var runInputs = BuildRunInputs(item.Inputs, executionMode, runIndex);
            var context = await _contextFactory.CreateAsync(
                session.Workflow,
                session.Execution,
                node,
                nodeType,
                runInputs,
                session.SuccessfulOutputs,
                session.LatestBatches,
                runIndex,
                cancellationToken).ConfigureAwait(false);

            var resolvedLlmClient = ResolveLlmClientForNode(node, nodeType, session.NodeMap, session.ConnectionsBySource, session.NodeLlmClients);
            if (resolvedLlmClient is not null)
            {
                context.LlmClient = resolvedLlmClient;
            }

            var result = await ExecuteNodeWithRetryAsync(node, nodeType, context, cancellationToken)
                .ConfigureAwait(false);

            if (context.LlmClient is not null)
            {
                session.NodeLlmClients[node.Id] = context.LlmClient;
            }

            var record = BuildNodeExecutionRecord(node.Id, runIndex, runInputs, result, context);

            session.Execution.NodeRecords = [.. session.Execution.NodeRecords, record];
            try
            {
                await session.DbContext.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            finalResult = result;

            if (!result.Success && node.ErrorStrategy != ErrorStrategy.Continue)
            {
                session.Execution.Status = ExecutionStatus.Failed;
                await session.DbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                session.WaitingArea.CleanupExecution(session.Execution.Id);
                return true;
            }
        }

        if (finalResult is null)
        {
            return false;
        }

        session.LatestBatches[node.Name] = finalResult.Output;
        if (finalResult.Success)
        {
            session.SuccessfulOutputs[node.Name] = finalResult.Output;
        }

        await RouteOutputsAsync(node, nodeType, finalResult, session, cancellationToken).ConfigureAwait(false);

        return false;
    }

    private async Task<NodeExecutionResult> ExecuteNodeWithRetryAsync(
        NodeDefinition node,
        INodeType nodeType,
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var maxRetries = node.ErrorStrategy == ErrorStrategy.Retry
            ? (node.RetryPolicy?.MaxRetries ?? 2)
            : 0;

        NodeExecutionResult result;
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                result = await nodeType.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "节点 {NodeName} ({NodeId}) 执行时发生异常。", node.Name, node.Id);
                var nodeError = new NodeError
                {
                    Code = ex.GetType().Name,
                    Message = ex.Message,
                    NodeDefinitionId = node.Id,
                    StackTrace = ex.StackTrace
                };
                result = new NodeExecutionResult
                {
                    Success = false,
                    Error = nodeError,
                    Output = new DataBatch
                    {
                        Items =
                        [
                            new DataItem
                            {
                                Success = false,
                                Error = nodeError
                            }
                        ]
                    }
                };
            }

            if (result.Success || attempt == maxRetries)
            {
                if (!result.Success && node.ErrorStrategy == ErrorStrategy.Continue)
                {
                    return _errorHandler.Handle(result, node.Id, ErrorStrategy.Continue);
                }

                return result;
            }

            var delay = CalculateBackoff(node.RetryPolicy, attempt);
            _logger.LogWarning(
                "节点 {NodeName} ({NodeId}) 第 {Attempt} 次执行失败，{Delay}ms 后重试。",
                node.Name,
                node.Id,
                attempt + 1,
                delay.TotalMilliseconds);

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("节点重试逻辑出现不可达路径。");
    }

    private async Task RouteOutputsAsync(
        NodeDefinition node,
        INodeType nodeType,
        NodeExecutionResult result,
        ExecutionSession session,
        CancellationToken cancellationToken)
    {
        var sourcePortName = ResolveSourcePortName(nodeType, result);
        var connections = session.ConnectionsBySource[(node.Id, sourcePortName)];

        foreach (var connection in connections)
        {
            if (!session.NodeMap.TryGetValue(connection.TargetNodeId, out var targetNode))
            {
                continue;
            }

            var targetNodeType = _nodeRegistry.Get(targetNode.TypeName);
            var targetInputPorts = GetInputPortNames(targetNodeType);
            var outputBatch = result.Output;

            if (targetInputPorts.Count <= 1)
            {
                var inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
                {
                    [connection.TargetPortName] = outputBatch
                };

                await session.Queue.EnqueueAsync(
                    new NodeWorkItem(session.Execution.Id, targetNode.Id, inputs),
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                session.WaitingArea.Receive(session.Execution.Id, targetNode.Id, connection.TargetPortName, outputBatch);

                if (session.WaitingArea.IsReady(session.Execution.Id, targetNode.Id, targetInputPorts))
                {
                    if (session.WaitingArea.TryTake(session.Execution.Id, targetNode.Id, out var readyInputs))
                    {
                        await session.Queue.EnqueueAsync(
                            new NodeWorkItem(session.Execution.Id, targetNode.Id, readyInputs),
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private async Task ProcessTimeoutsAsync(
        ExecutionSession session,
        CancellationToken cancellationToken)
    {
        foreach (var (executionId, nodeInstanceId) in session.WaitingArea.GetTimeoutKeys().ToList())
        {
            if (executionId != session.Execution.Id)
            {
                continue;
            }

            if (!session.NodeMap.TryGetValue(nodeInstanceId, out var node))
            {
                session.WaitingArea.CancelWaiting(executionId, nodeInstanceId);
                continue;
            }

            session.WaitingArea.TryTake(executionId, nodeInstanceId, out _);

            var timeoutResult = _errorHandler.CreateInputTimeoutResult(node.Id);
            if (node.ErrorStrategy == ErrorStrategy.Continue)
            {
                timeoutResult = _errorHandler.Handle(timeoutResult, node.Id, ErrorStrategy.Continue);
            }

            var record = BuildNodeExecutionRecord(
                node.Id,
                runIndex: 0,
                inputs: new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase),
                output: timeoutResult,
                rawParameters: new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase),
                resolvedParameters: new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase));

            session.Execution.NodeRecords = [.. session.Execution.NodeRecords, record];
            try
            {
                await session.DbContext.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            if (node.ErrorStrategy != ErrorStrategy.Continue)
            {
                session.Execution.Status = ExecutionStatus.Failed;
                await session.DbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                session.WaitingArea.CleanupExecution(session.Execution.Id);
                return;
            }

            var nodeType = _nodeRegistry.Get(node.TypeName);
            await RouteOutputsAsync(node, nodeType, timeoutResult, session, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ResolveSourcePortName(INodeType nodeType, NodeExecutionResult result)
    {
        if (result.BranchIndex.HasValue)
        {
            var outputPorts = GetOutputPortNames(nodeType);
            var index = result.BranchIndex.Value;
            if (index >= 0 && index < outputPorts.Count)
            {
                return outputPorts[index];
            }
        }

        return FlowConstants.PortNames.Output;
    }

    private static IReadOnlyDictionary<string, DataBatch> BuildRunInputs(
        IReadOnlyDictionary<string, DataBatch> inputs,
        ExecutionMode mode,
        int runIndex)
    {
        if (mode != ExecutionMode.OncePerItem)
        {
            return inputs;
        }

        var result = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);
        foreach (var (portName, batch) in inputs)
        {
            if (runIndex < batch.Items.Count)
            {
                result[portName] = new DataBatch
                {
                    Items = [batch.Items[runIndex]]
                };
            }
            else
            {
                result[portName] = new DataBatch();
            }
        }

        return result;
    }

    private static DataBatch CreateDataBatch(object? payload)
    {
        if (payload is DataBatch batch) return batch;
        if (payload is DataItem item) return new DataBatch { Items = [item] };

        if (payload is null)
        {
            return new DataBatch
            {
                Items =
                [
                    new DataItem { Data = null, Success = true, SourceIndex = 0 }
                ]
            };
        }

        if (payload is IEnumerable enumerable && payload is not string)
        {
            var items = new List<DataItem>();
            var index = 0;
            foreach (var value in enumerable)
            {
                items.Add(new DataItem
                {
                    Data = JsonSerializer.SerializeToNode(value, JsonDefaults.Options),
                    Success = true,
                    SourceIndex = index++
                });
            }
            return new DataBatch { Items = items };
        }

        var data = JsonSerializer.SerializeToNode(payload, JsonDefaults.Options);
        return new DataBatch
        {
            Items =
            [
                new DataItem { Data = data, Success = true, SourceIndex = 0 }
            ]
        };
    }

    private static TimeSpan CalculateBackoff(RetryPolicy? policy, int attempt)
    {
        var baseSeconds = Math.Pow(2, attempt);
        var maxDelay = policy?.MaxDelay.TotalSeconds > 0
            ? policy.MaxDelay
            : TimeSpan.FromSeconds(60);

        var delay = TimeSpan.FromSeconds(Math.Min(baseSeconds, maxDelay.TotalSeconds));

        if (policy?.UseJitter == true)
        {
            var jitter = Random.Shared.NextDouble() * delay.TotalMilliseconds;
            delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds + jitter);
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds, maxDelay.TotalMilliseconds));
        }

        return delay;
    }

    private static IReadOnlyList<string> GetInputPortNames(INodeType nodeType)
    {
        return nodeType.Ports
            .Where(p => p.Direction == PortDirection.Input)
            .Select(p => p.Name)
            .ToList();
    }

    private static IReadOnlyList<string> GetOutputPortNames(INodeType nodeType)
    {
        return nodeType.Ports
            .Where(p => p.Direction == PortDirection.Output)
            .Select(p => p.Name)
            .ToList();
    }

    private static ILlmClient? ResolveLlmClientForNode(
        NodeDefinition node,
        INodeType nodeType,
        Dictionary<Guid, NodeDefinition> nodeMap,
        ILookup<(Guid SourceNodeId, string SourcePortName), Connection> connectionsBySource,
        ConcurrentDictionary<Guid, ILlmClient> nodeLlmClients)
    {
        var supplyInputPorts = nodeType.Ports
            .Where(p => p.Direction == PortDirection.Input && p.Type == PortType.LLM)
            .ToList();

        if (supplyInputPorts.Count == 0)
        {
            return null;
        }

        foreach (var port in supplyInputPorts)
        {
            var incomingConnections = connectionsBySource
                .Where(g => g.Key.SourceNodeId != node.Id)
                .SelectMany(g => g)
                .Where(c => c.TargetNodeId == node.Id && c.TargetPortName == port.Name)
                .ToList();

            foreach (var connection in incomingConnections)
            {
                if (nodeLlmClients.TryGetValue(connection.SourceNodeId, out var client))
                {
                    return client;
                }
            }
        }

        return null;
    }

    private static NodeExecutionRecord BuildNodeExecutionRecord(
        Guid nodeDefinitionId,
        int runIndex,
        IReadOnlyDictionary<string, DataBatch> inputs,
        NodeExecutionResult output,
        NodeExecutionContext context)
    {
        return BuildNodeExecutionRecord(
            nodeDefinitionId, runIndex, inputs, output,
            context.RawParameters, context.ResolvedParameters);
    }

    private static NodeExecutionRecord BuildNodeExecutionRecord(
        Guid nodeDefinitionId,
        int runIndex,
        IReadOnlyDictionary<string, DataBatch> inputs,
        NodeExecutionResult output,
        IReadOnlyDictionary<string, object> rawParameters,
        IReadOnlyDictionary<string, object> resolvedParameters)
    {
        return new NodeExecutionRecord
        {
            NodeDefinitionId = nodeDefinitionId,
            RunIndex = runIndex,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Inputs = inputs.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            Output = output,
            RawParameters = rawParameters.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            ResolvedParameters = resolvedParameters.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
        };
    }
}
