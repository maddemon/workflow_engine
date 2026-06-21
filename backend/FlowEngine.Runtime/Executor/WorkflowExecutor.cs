using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.WaitingArea;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 工作流执行引擎主循环实现。
/// </summary>
public sealed class WorkflowExecutor : IEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IWorkflowRepository _workflowRepository;
    private readonly IExecutionStore _executionStore;
    private readonly INodeRegistry _nodeRegistry;
    private readonly NodeExecutionContextFactory _contextFactory;
    private readonly ErrorStrategyHandler _errorHandler;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowExecutor> _logger;

    /// <summary>
    /// 初始化执行引擎。
    /// </summary>
    public WorkflowExecutor(
        IWorkflowRepository workflowRepository,
        IExecutionStore executionStore,
        INodeRegistry nodeRegistry,
        NodeExecutionContextFactory contextFactory,
        ErrorStrategyHandler errorHandler,
        IServiceScopeFactory scopeFactory,
        ILogger<WorkflowExecutor> logger)
    {
        _workflowRepository = workflowRepository ?? throw new ArgumentNullException(nameof(workflowRepository));
        _executionStore = executionStore ?? throw new ArgumentNullException(nameof(executionStore));
        _nodeRegistry = nodeRegistry ?? throw new ArgumentNullException(nameof(nodeRegistry));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ExecutionId> StartAsync(
        Guid workflowDefinitionId,
        object? triggerPayload = null,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _workflowRepository.GetByIdAsync(workflowDefinitionId, cancellationToken)
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

        await _executionStore.SaveAsync(executionRecord, cancellationToken).ConfigureAwait(false);

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var scopedExecutionStore = scope.ServiceProvider.GetRequiredService<IExecutionStore>();

            try
            {
                await ExecuteLoopAsync(workflow, executionRecord, triggerPayload, scopedExecutionStore, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("执行 {ExecutionId} 已取消。", executionRecord.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行 {ExecutionId} 发生未处理异常。", executionRecord.Id);
                await scopedExecutionStore.UpdateStatusAsync(executionRecord.Id, ExecutionStatus.Failed, default)
                    .ConfigureAwait(false);
            }
        }, CancellationToken.None);

        return ExecutionId.From(executionRecord.Id);
    }

    /// <inheritdoc />
    public Task ResumeAsync(ExecutionId executionId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("MVP 阶段暂不支持恢复执行。");
    }

    private async Task ExecuteLoopAsync(
        Workflow workflow,
        ExecutionRecord execution,
        object? triggerPayload,
        IExecutionStore executionStore,
        CancellationToken cancellationToken)
    {
        var queue = new ExecutionQueue();
        var waitingArea = new WaitingArea.WaitingArea();
        var stateMachine = new ExecutionStateMachine(ExecutionStatus.Pending);
        var nodeOutputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);
        var nodeBatches = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);
        var nodeLlmClients = new Dictionary<Guid, ILlmClient>();

        var nodeMap = workflow.Nodes.ToDictionary(n => n.Id);
        var connectionsBySource = workflow.Connections
            .ToLookup(c => (c.SourceNodeId, c.SourcePortName));

        stateMachine.Start();
        await executionStore.UpdateStatusAsync(execution.Id, ExecutionStatus.Running, cancellationToken)
            .ConfigureAwait(false);

        await EnqueueEntryNodesAsync(
            workflow,
            execution,
            queue,
            triggerPayload,
            cancellationToken).ConfigureAwait(false);

        const int IdleDelayMilliseconds = 500;

        while (!cancellationToken.IsCancellationRequested)
        {
            await ProcessTimeoutsAsync(
                workflow,
                execution,
                nodeMap,
                connectionsBySource,
                waitingArea,
                queue,
                nodeOutputs,
                nodeBatches,
                executionStore,
                cancellationToken).ConfigureAwait(false);

            if (queue.Reader.TryRead(out var item))
            {
                var shouldStop = await ProcessNodeAsync(
                    item,
                    workflow,
                    execution,
                    nodeMap,
                    connectionsBySource,
                    queue,
                    waitingArea,
                    nodeOutputs,
                    nodeBatches,
                    nodeLlmClients,
                    executionStore,
                    cancellationToken).ConfigureAwait(false);

                if (shouldStop)
                {
                    stateMachine.Fail();
                    break;
                }

                continue;
            }

            if (waitingArea.IsEmpty)
            {
                break;
            }

            await Task.Delay(IdleDelayMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            stateMachine.Cancel();
            waitingArea.CleanupExecution(execution.Id);
        }
        else if (stateMachine.Status == ExecutionStatus.Running)
        {
            stateMachine.Complete();
        }

        await executionStore.SaveAsync(execution, cancellationToken).ConfigureAwait(false);
        await executionStore.UpdateStatusAsync(execution.Id, stateMachine.Status, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnqueueEntryNodesAsync(
        Workflow workflow,
        ExecutionRecord execution,
        ExecutionQueue queue,
        object? triggerPayload,
        CancellationToken cancellationToken)
    {
        var triggerBatch = CreateDataBatch(triggerPayload);
        var hasInputConnections = workflow.Connections
            .Select(c => c.TargetNodeId)
            .ToHashSet();

        foreach (var node in workflow.Nodes)
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

            await queue.EnqueueAsync(
                new NodeWorkItem(execution.Id, node.Id, inputs),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> ProcessNodeAsync(
        NodeWorkItem item,
        Workflow workflow,
        ExecutionRecord execution,
        Dictionary<Guid, NodeInstance> nodeMap,
        ILookup<(Guid SourceNodeId, string SourcePortName), Connection> connectionsBySource,
        ExecutionQueue queue,
        WaitingArea.WaitingArea waitingArea,
        Dictionary<string, DataBatch> nodeOutputs,
        Dictionary<string, DataBatch> nodeBatches,
        Dictionary<Guid, ILlmClient> nodeLlmClients,
        IExecutionStore executionStore,
        CancellationToken cancellationToken)
    {
        if (!nodeMap.TryGetValue(item.NodeInstanceId, out var node))
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
                workflow,
                execution,
                node,
                nodeType,
                runInputs,
                nodeOutputs,
                nodeBatches,
                runIndex,
                cancellationToken).ConfigureAwait(false);

            var resolvedLlmClient = ResolveLlmClientForNode(node, nodeType, nodeMap, connectionsBySource, nodeLlmClients);
            if (resolvedLlmClient is not null)
            {
                context.LlmClient = resolvedLlmClient;
            }

            var result = await ExecuteNodeWithRetryAsync(node, nodeType, context, cancellationToken)
                .ConfigureAwait(false);

            if (result.LlmClient is not null)
            {
                nodeLlmClients[node.Id] = result.LlmClient;
            }

            var record = new NodeExecutionRecord
            {
                NodeDefinitionId = node.Id,
                RunIndex = runIndex,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
                Inputs = runInputs,
                Output = result,
                RawParameters = context.RawParameters,
                ResolvedParameters = context.ResolvedParameters
            };

            execution.NodeRecords.Add(record);
            await executionStore.AddNodeRecordAsync(execution.Id, record, cancellationToken)
                .ConfigureAwait(false);
            await executionStore.SaveAsync(execution, cancellationToken)
                .ConfigureAwait(false);

            finalResult = result;

            if (!result.Success && node.ErrorStrategy != ErrorStrategy.Continue)
            {
                await executionStore.UpdateStatusAsync(execution.Id, ExecutionStatus.Failed, cancellationToken)
                    .ConfigureAwait(false);
                waitingArea.CleanupExecution(execution.Id);
                return true;
            }
        }

        if (finalResult is null)
        {
            return false;
        }

        nodeBatches[node.Name] = finalResult.Output;
        if (finalResult.Success)
        {
            nodeOutputs[node.Name] = finalResult.Output;
        }

        await RouteOutputsAsync(
            node,
            nodeType,
            finalResult,
            execution.Id,
            nodeMap,
            connectionsBySource,
            queue,
            waitingArea,
            cancellationToken).ConfigureAwait(false);

        return false;
    }

    private async Task<NodeExecutionResult> ExecuteNodeWithRetryAsync(
        NodeInstance node,
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

        // 循环内部已处理所有返回路径，此处不会执行。
        throw new InvalidOperationException("节点重试逻辑出现不可达路径。");
    }

    private async Task RouteOutputsAsync(
        NodeInstance node,
        INodeType nodeType,
        NodeExecutionResult result,
        Guid executionId,
        Dictionary<Guid, NodeInstance> nodeMap,
        ILookup<(Guid SourceNodeId, string SourcePortName), Connection> connectionsBySource,
        ExecutionQueue queue,
        WaitingArea.WaitingArea waitingArea,
        CancellationToken cancellationToken)
    {
        var sourcePortName = ResolveSourcePortName(nodeType, result);
        var connections = connectionsBySource[(node.Id, sourcePortName)];

        foreach (var connection in connections)
        {
            if (!nodeMap.TryGetValue(connection.TargetNodeId, out var targetNode))
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

                await queue.EnqueueAsync(
                    new NodeWorkItem(executionId, targetNode.Id, inputs),
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                waitingArea.Receive(executionId, targetNode.Id, connection.TargetPortName, outputBatch);

                if (waitingArea.IsReady(executionId, targetNode.Id, targetInputPorts))
                {
                    if (waitingArea.TryTake(executionId, targetNode.Id, out var readyInputs))
                    {
                        await queue.EnqueueAsync(
                            new NodeWorkItem(executionId, targetNode.Id, readyInputs),
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private async Task ProcessTimeoutsAsync(
        Workflow workflow,
        ExecutionRecord execution,
        Dictionary<Guid, NodeInstance> nodeMap,
        ILookup<(Guid SourceNodeId, string SourcePortName), Connection> connectionsBySource,
        WaitingArea.WaitingArea waitingArea,
        ExecutionQueue queue,
        Dictionary<string, DataBatch> nodeOutputs,
        Dictionary<string, DataBatch> nodeBatches,
        IExecutionStore executionStore,
        CancellationToken cancellationToken)
    {
        foreach (var (executionId, nodeInstanceId) in waitingArea.GetTimeoutKeys().ToList())
        {
            if (executionId != execution.Id)
            {
                continue;
            }

            if (!nodeMap.TryGetValue(nodeInstanceId, out var node))
            {
                waitingArea.CancelWaiting(executionId, nodeInstanceId);
                continue;
            }

            waitingArea.TryTake(executionId, nodeInstanceId, out _);

            var timeoutResult = _errorHandler.CreateInputTimeoutResult(node.Id);
            if (node.ErrorStrategy == ErrorStrategy.Continue)
            {
                timeoutResult = _errorHandler.Handle(timeoutResult, node.Id, ErrorStrategy.Continue);
            }

            var record = new NodeExecutionRecord
            {
                NodeDefinitionId = node.Id,
                RunIndex = 0,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
                Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase),
                Output = timeoutResult,
                RawParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase),
                ResolvedParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            };

            execution.NodeRecords.Add(record);
            await executionStore.AddNodeRecordAsync(execution.Id, record, cancellationToken)
                .ConfigureAwait(false);
            await executionStore.SaveAsync(execution, cancellationToken)
                .ConfigureAwait(false);

            if (node.ErrorStrategy != ErrorStrategy.Continue)
            {
                await executionStore.UpdateStatusAsync(execution.Id, ExecutionStatus.Failed, cancellationToken)
                    .ConfigureAwait(false);
                waitingArea.CleanupExecution(execution.Id);
                return;
            }

            var nodeType = _nodeRegistry.Get(node.TypeName);
            await RouteOutputsAsync(
                node,
                nodeType,
                timeoutResult,
                executionId,
                nodeMap,
                connectionsBySource,
                queue,
                waitingArea,
                cancellationToken).ConfigureAwait(false);
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

        return "output";
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
        if (payload is DataBatch batch)
        {
            return batch;
        }

        if (payload is DataItem item)
        {
            return new DataBatch { Items = [item] };
        }

        if (payload is null)
        {
            return new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = null,
                        Success = true,
                        SourceIndex = 0
                    }
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
                    Data = JsonSerializer.SerializeToNode(value, JsonOptions),
                    Success = true,
                    SourceIndex = index++
                });
            }

            return new DataBatch { Items = items };
        }

        var data = JsonSerializer.SerializeToNode(payload, JsonOptions);
        return new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = data,
                    Success = true,
                    SourceIndex = 0
                }
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
        NodeInstance node,
        INodeType nodeType,
        Dictionary<Guid, NodeInstance> nodeMap,
        ILookup<(Guid SourceNodeId, string SourcePortName), Connection> connectionsBySource,
        Dictionary<Guid, ILlmClient> nodeLlmClients)
    {
        var supplyInputPorts = nodeType.Ports
            .Where(p => p.Direction == PortDirection.Input && p.Type == PortType.LLMSupply)
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
}
