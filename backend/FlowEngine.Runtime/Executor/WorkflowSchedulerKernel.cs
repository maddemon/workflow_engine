using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using FlowEngine.Core;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Security;
using FlowEngine.Runtime.WaitingArea;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 纯内存工作流调度内核：驱动队列节点处理、输出路由、等待区与超时。
/// 不依赖 <c>DbContext</c> 与事件总线；持久化与事件发布经
/// <see cref="IExecutionSideEffects"/> 回调交由外壳处理，从而可被普通执行与 Dry-Run 复用。
/// </summary>
public sealed class WorkflowSchedulerKernel(
    INodeRegistry nodeRegistry,
    NodeExecutionContextFactory contextFactory,
    ErrorStrategyHandler errorHandler,
    SecretMasker secretMasker,
    ILogger<WorkflowSchedulerKernel> logger,
    IOptions<EngineDefaultsOptions>? defaultsOptions = null)
{
    private readonly EngineDefaultsOptions _defaults = defaultsOptions?.Value ?? new EngineDefaultsOptions();

    /// <summary>
    /// 执行调度循环：驱动入口节点入队、逐节点处理、输出路由、等待区超时，直至完成或取消。
    /// </summary>
    /// <param name="session">执行会话（纯内存可变状态）。</param>
    /// <param name="sideEffects">副作用回调（持久化 / 事件发布）。</param>
    /// <param name="triggerPayload">触发负载。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RunAsync(
        ExecutionSession session,
        IExecutionSideEffects sideEffects,
        object? triggerPayload,
        CancellationToken cancellationToken)
    {
        session.StateMachine.Start();

        await EnqueueEntryNodesAsync(session, triggerPayload, cancellationToken).ConfigureAwait(false);

        const int IdleDelayMilliseconds = 500;
        var cancelled = false;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await ProcessTimeoutsAsync(session, sideEffects, cancellationToken).ConfigureAwait(false);

                if (session.Queue.Reader.TryRead(out var item))
                {
                    var shouldStop = await ProcessNodeAsync(item, session, sideEffects, cancellationToken).ConfigureAwait(false);

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

                try
                {
                    await Task.Delay(IdleDelayMilliseconds, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 空闲等待期间被取消，退出循环交由下方统一处理 Cancelled。
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 节点处理 / 超时 / 副作用回调期间被取消：捕获后统一转为 Cancelled 终态，不向上抛。
            cancelled = true;
        }

        if (cancelled || cancellationToken.IsCancellationRequested)
        {
            session.StateMachine.Cancel();
            session.WaitingArea.CleanupExecution(session.Execution.Id);
        }
        else if (session.StateMachine.Status == ExecutionStatus.Running)
        {
            session.StateMachine.Complete();
        }

        session.Execution.Status = session.StateMachine.Status;
        if (session.StateMachine.Status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
        {
            session.Execution.CompletedAt = DateTime.UtcNow;
        }

        // 终态持久化与事件发布必须在取消后仍完成（保存 Cancelled/Completed 终态），
        // 故使用 CancellationToken.None：此时 cancellationToken 可能已取消，若传入会导致 SaveChangesAsync
        // 抛出而终态丢失。真实的取消传播已由逐节点的 PersistNodeRecordAsync 与失败态 PersistFailedStateAsync 承担。
        await sideEffects.PersistExecutionAsync(CancellationToken.None).ConfigureAwait(false);

        await sideEffects.PublishCompletedAsync(session.StateMachine.Status, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task EnqueueEntryNodesAsync(
        ExecutionSession session,
        object? triggerPayload,
        CancellationToken cancellationToken)
    {
        var triggerBatch = CreateDataBatch(triggerPayload);
        var hasInputConnections = session.Workflow.Connections
            .Select(c => c.TargetNodeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var node in session.Workflow.Nodes)
        {
            var nodeType = nodeRegistry.Get(node.TypeName);
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
        IExecutionSideEffects sideEffects,
        CancellationToken cancellationToken)
    {
        if (!session.NodeMap.TryGetValue(item.NodeInstanceId, out var node))
        {
            return false;
        }

        var nodeType = nodeRegistry.Get(node.TypeName);
        var executionMode = nodeType.ExecutionMode;
        var runCount = executionMode == ExecutionMode.OncePerItem
            ? Math.Max(1, item.Inputs.Values.DefaultIfEmpty(new DataBatch()).Max(b => b.Items.Count))
            : 1;

        // 节点上下文生命周期（节点级持久化上下文方案）：
        // 非回边激活（新上游输入）→ 清空旧上下文，GetOrAdd 将创建全新状态；
        // 回边激活（环路继续）→ 保留上下文，复用既有迭代状态（LoopNode 的正常循环依赖此路径）。
        if (!item.IsFeedbackActivation)
        {
            session.NodeContexts.TryRemove(node.Id, out _);
            // 新上游输入开启新一轮循环，重置反馈激活计数（见下方环路失控保护）。
            session.FeedbackActivationCounts.TryRemove(node.Id, out _);
        }
        else
        {
            // 环路失控保护：反馈边激活累计超过上限 → 判定为无限回环，转 Failed。
            // 仅依赖节点自身终止条件（如 LoopNode 的 position 单调递增）不足以防住基于 $nodeContext
            // 的任意回环（计数器未达阈值等），故设全局安全网（MaxCycleIterations）。
            var feedbackCount = session.FeedbackActivationCounts.AddOrUpdate(node.Id, 1, (_, v) => v + 1);
            if (_defaults.MaxCycleIterations > 0 && feedbackCount > _defaults.MaxCycleIterations)
            {
                var limitError = new NodeError
                {
                    Code = "CycleLimitExceeded",
                    Message = $"节点 {node.Name} ({node.Id}) 反馈边激活次数达 {feedbackCount}，超过上限 {_defaults.MaxCycleIterations}，判定为环路失控。",
                    NodeDefinitionId = node.Id
                };
                var limitRecord = new NodeExecutionRecord
                {
                    Id = Guid.NewGuid(),
                    NodeDefinitionId = node.Id,
                    RunIndex = 0,
                    StartedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow,
                    Output = new NodeExecutionResult { Success = false, Error = limitError }
                };
                session.Execution.NodeRecords.Add(limitRecord);
                await sideEffects.PersistNodeRecordAsync(limitRecord, cancellationToken).ConfigureAwait(false);
                session.Execution.Status = ExecutionStatus.Failed;
                session.Execution.CompletedAt = DateTime.UtcNow;
                await sideEffects.PersistFailedStateAsync(cancellationToken).ConfigureAwait(false);
                session.WaitingArea.CleanupExecution(session.Execution.Id);
                return true; // shouldStop
            }
        }

        var nodeContext = session.NodeContexts.GetOrAdd(
            node.Id,
            _ => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));

        NodeExecutionResult? finalResult = null;
        // 累积本节点本次调用的各次成功运行输出（OncePerItem 会按批次多次运行同一节点），
        // 全部项都需进入 SuccessfulOutputs / LatestBatches，供下游 $node.<name> / $items(<name>) 读取。
        var accumulatedItems = new List<DataItem>();

        for (var runIndex = 0; runIndex < runCount; runIndex++)
        {
            var runInputs = BuildRunInputs(item.Inputs, executionMode, runIndex);
            NodeExecutionContext? context = null;
            try
            {
                context = await contextFactory.CreateAsync(
                    session.Workflow,
                    session.Execution,
                    node,
                    nodeType,
                    runInputs,
                    session.SuccessfulOutputs,
                    session.LatestBatches,
                    runIndex,
                    cancellationToken,
                    session.CredentialAccessor,
                    nodeContext: nodeContext).ConfigureAwait(false);
            }
            catch (ScriptErrorException ex)
            {
                var failureResult = new NodeExecutionResult
                {
                    Success = false,
                    Error = new NodeError
                    {
                        Code = "ScriptParameterPreEvaluationError",
                        Message = ex.Message,
                        NodeDefinitionId = node.Id.ToString(),
                    },
                };

                var failedRecord = new NodeExecutionRecord
                {
                    Id = Guid.NewGuid(),
                    NodeDefinitionId = node.Id,
                    RunIndex = runIndex,
                    StartedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow,
                    Inputs = runInputs.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                    Output = failureResult,
                };

                session.Execution.NodeRecords.Add(failedRecord);
                await sideEffects.PublishNodeStartedAsync(session.Execution.Id, node.Id, runIndex, cancellationToken).ConfigureAwait(false);
                await sideEffects.PersistNodeRecordAsync(failedRecord, cancellationToken).ConfigureAwait(false);

                finalResult = failureResult;
                continue; // skip to next runIndex
            }
            context.NodeExecutionRecordId = Guid.NewGuid();
            context.Memory = session.Memory;

            var resolvedLlmClient = ResolveLlmClientForNode(node, nodeType, session.NodeMap, session.ConnectionsBySource, session.NodeLlmClients);
            if (resolvedLlmClient is not null)
            {
                context.LlmClient = resolvedLlmClient;
            }

            context.OnLlmStreamChunk = sideEffects.CreateLlmStreamCallback(session.Execution.Id, node.Id, runIndex);

            await sideEffects.PublishNodeStartedAsync(session.Execution.Id, node.Id, runIndex, cancellationToken)
                .ConfigureAwait(false);

            NodeExecutionResult result;
            try
            {
                result = await ExecuteNodeWithRetryAsync(node, nodeType, context, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                // 释放节点执行期间托管的 JS 引擎（含重试循环结束后统一释放）。
                context.ReleaseEngine();
            }

            if (context.LlmClient is not null)
            {
                session.NodeLlmClients[node.Id] = context.LlmClient;
            }

            var record = BuildNodeExecutionRecord(node.Id, runIndex, runInputs, result, context, session.SensitiveValues);

            session.Execution.NodeRecords.Add(record);
            await sideEffects.PersistNodeRecordAsync(record, cancellationToken).ConfigureAwait(false);

            finalResult = result;

            // 累积成功运行的输出项：OncePerItem 多次运行须全部保留，而非仅最后一次。
            if (result.Success)
            {
                accumulatedItems.AddRange(result.Output.Items);
            }

            if (!result.Success && node.ErrorStrategy != ErrorStrategy.Continue)
            {
                // 取消优先：若已请求取消，交由 RunAsync 外层统一转为 Cancelled，
                // 避免取消中的节点被误判为 Failed 而丢失取消语义。
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                session.Execution.Status = ExecutionStatus.Failed;
                session.Execution.CompletedAt = DateTime.UtcNow;
                await sideEffects.PersistFailedStateAsync(cancellationToken).ConfigureAwait(false);
                session.WaitingArea.CleanupExecution(session.Execution.Id);
                return true;
            }
        }

        if (finalResult is null)
        {
            return false;
        }

        // 已请求取消：不再路由输出、不再覆写状态，交由 RunAsync 外层统一落库 Cancelled。
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        // 累积本节点本次调用的全部成功运行输出（OncePerItem 按批次多次运行同一节点，
        // 覆盖式赋值会丢失其余项，导致下游 $node.<name> / $items(<name>) 只看到最后一项）。
        if (accumulatedItems.Count > 0)
        {
            var cumulative = new DataBatch { Items = accumulatedItems.ToList() };
            session.LatestBatches[node.Name] = cumulative;

            // 无条件写入 SuccessfulOutputs：节点每次成功运行的输出都作为该节点的成功结果累积，
            // 供下游经 $node.<name> / $items(<name>) 读取。BranchIndex 仅标识输出端口，
            // 不能据此丢弃输出——否则 IfNode 的 true 分支、SwitchNode 的 case 0 等 BranchIndex = 0 的
            // 合法输出会被静默丢弃。先前基于「O(N²)」的 BranchIndex != 0 守卫为错误分析的产物，已移除。
            var priorItems = session.SuccessfulOutputs.TryGetValue(node.Name, out var prior) && prior.Items.Count > 0
                ? prior.Items
                : [];
            session.SuccessfulOutputs[node.Name] = new DataBatch
            {
                Items = priorItems.Concat(accumulatedItems).ToList()
            };
        }
        else
        {
            // 无任何成功运行（全部失败等）：保留原有语义，仅刷新 LatestBatches 为最终批，不写入 SuccessfulOutputs。
            session.LatestBatches[node.Name] = finalResult.Output;
        }

        // OncePerItem：下游边消费的是累积批（全部项）而非最后一次运行的单批，避免静默丢数据。
        // finalResult 在上方 `if (finalResult is null) return false;` 后已确定非 null。
        var resultForRouting = accumulatedItems.Count > 0
            ? new NodeExecutionResult
            {
                Success = finalResult!.Success,
                Output = session.LatestBatches[node.Name],
                BranchIndex = finalResult!.BranchIndex,
                Error = finalResult!.Error,
                ToolExecutionRecords = finalResult!.ToolExecutionRecords,
            }
            : finalResult!;
        await RouteOutputsAsync(node, nodeType, resultForRouting, session, cancellationToken).ConfigureAwait(false);

        return false;
    }

    private async Task<NodeExecutionResult> ExecuteNodeWithRetryAsync(
        NodeDefinition node,
        INodeType nodeType,
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var maxRetries = node.RetryPolicy?.MaxRetries
            ?? (node.ErrorStrategy == ErrorStrategy.Retry ? Math.Max(_defaults.DefaultMaxRetries, 1) : _defaults.DefaultMaxRetries);

        var effectiveTimeout = node.Timeout
            ?? (_defaults.DefaultTimeoutSeconds.HasValue ? TimeSpan.FromSeconds(_defaults.DefaultTimeoutSeconds.Value) : null);

        NodeExecutionResult result;
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            CancellationTokenSource? timeoutCts = null;
            try
            {
                var effectiveToken = cancellationToken;
                if (effectiveTimeout is { } timeout && timeout > TimeSpan.Zero)
                {
                    timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(timeout);
                    effectiveToken = timeoutCts.Token;
                }

                result = await nodeType.ExecuteAsync(context, effectiveToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts is not null && timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // 节点超时，不重试，直接返回超时错误。
                var timeoutError = new NodeError
                {
                    Code = "Timeout",
                    Message = $"节点执行超时，超时时间：{effectiveTimeout!.Value.TotalMilliseconds}ms。",
                    NodeDefinitionId = node.Id
                };
                return new NodeExecutionResult
                {
                    Success = false,
                    Error = timeoutError,
                    Output = new DataBatch
                    {
                        Items =
                        [
                            new DataItem
                            {
                                Success = false,
                                Error = timeoutError
                            }
                        ]
                    }
                };
            }
            catch (OperationCanceledException)
            {
                // 取消异常不重试，直接返回取消结果由上层错误策略处理。
                var cancelledError = new NodeError
                {
                    Code = "Cancelled",
                    Message = "节点执行被取消。",
                    NodeDefinitionId = node.Id
                };
                return new NodeExecutionResult
                {
                    Success = false,
                    Error = cancelledError,
                    Output = new DataBatch
                    {
                        Items =
                        [
                            new DataItem
                            {
                                Success = false,
                                Error = cancelledError
                            }
                        ]
                    }
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "节点 {NodeName} ({NodeId}) 执行时发生异常。", node.Name, node.Id);
                var nodeError = new NodeError
                {
                    Code = "NodeExecutionFailed",
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
            finally
            {
                timeoutCts?.Dispose();
            }

            // 检查可重试错误码过滤
            if (!result.Success && node.RetryPolicy?.RetryableErrorCodes?.Count > 0)
            {
                var errorCode = result.Error?.Code ?? string.Empty;
                if (!node.RetryPolicy.RetryableErrorCodes.Contains(errorCode))
                {
                    return result; // 错误码不在可重试列表中，直接返回不重试
                }
            }

            if (result.Success || attempt == maxRetries)
            {
                if (!result.Success && node.ErrorStrategy == ErrorStrategy.Continue)
                {
                    return errorHandler.Handle(result, node.Id, ErrorStrategy.Continue);
                }

                return result;
            }

            var delay = CalculateBackoff(node.RetryPolicy, attempt, _defaults);
            logger.LogWarning(
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
        var sourceKey = (node.Id, sourcePortName.ToLowerInvariant());
        var connections = session.ConnectionsBySource.Contains(sourceKey)
            ? session.ConnectionsBySource[sourceKey]
            : Enumerable.Empty<Connection>();
        var connectionList = connections.ToList();

        foreach (var connection in connectionList)
        {
            if (!session.NodeMap.TryGetValue(connection.TargetNodeId, out var targetNode))
            {
                logger.LogWarning(
                    "RouteOutputsAsync: 目标节点 {TargetNodeId} 不存在，跳过连接 {ConnectionId}。",
                    connection.TargetNodeId,
                    connection.Id);
                continue;
            }

            var targetNodeType = nodeRegistry.Get(targetNode.TypeName);
            var targetInputPorts = GetInputPortNames(targetNodeType);
            var outputBatch = result.Output;

            // 当 TargetPortName 为 null 时，解析为目标节点的第一个输入端口名。
            var resolvedTargetPort = connection.TargetPortName;
            if (string.IsNullOrEmpty(resolvedTargetPort) && targetInputPorts.Count > 0)
            {
                resolvedTargetPort = targetInputPorts[0];
            }

            // 标记该次激活是否来自环路回边（用于节点上下文重置判定，见 Task 9）。
            var isFeedback = session.FeedbackEdgeKeys.Contains(
                (connection.SourceNodeId, connection.SourcePortName, connection.TargetNodeId, connection.TargetPortName));

            if (targetInputPorts.Count <= 1)
            {
                var inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
                {
                    [resolvedTargetPort ?? FlowConstants.PortNames.Input] = outputBatch
                };

                await session.Queue.EnqueueAsync(
                    new NodeWorkItem(session.Execution.Id, targetNode.Id, inputs, IsFeedbackActivation: isFeedback),
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                session.WaitingArea.Receive(session.Execution.Id, targetNode.Id, resolvedTargetPort ?? FlowConstants.PortNames.Input, outputBatch);

                if (session.WaitingArea.IsReady(session.Execution.Id, targetNode.Id, targetInputPorts))
                {
                    if (session.WaitingArea.TryTake(session.Execution.Id, targetNode.Id, out var readyInputs))
                    {
                        await session.Queue.EnqueueAsync(
                            new NodeWorkItem(session.Execution.Id, targetNode.Id, readyInputs, IsFeedbackActivation: isFeedback),
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private async Task ProcessTimeoutsAsync(
        ExecutionSession session,
        IExecutionSideEffects sideEffects,
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

            var timeoutResult = errorHandler.CreateInputTimeoutResult(node.Id);
            if (node.ErrorStrategy == ErrorStrategy.Continue)
            {
                timeoutResult = errorHandler.Handle(timeoutResult, node.Id, ErrorStrategy.Continue);
            }

            var record = BuildNodeExecutionRecord(
                node.Id,
                runIndex: 0,
                inputs: new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase),
                output: timeoutResult,
                rawParameters: new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase),
                resolvedParameters: new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase),
                sensitiveValues: session.SensitiveValues);

            session.Execution.NodeRecords.Add(record);
            await sideEffects.PersistNodeRecordAsync(record, cancellationToken).ConfigureAwait(false);

            if (node.ErrorStrategy != ErrorStrategy.Continue)
            {
                session.Execution.Status = ExecutionStatus.Failed;
                session.Execution.CompletedAt = DateTime.UtcNow;
                await sideEffects.PersistFailedStateAsync(cancellationToken).ConfigureAwait(false);
                session.WaitingArea.CleanupExecution(session.Execution.Id);
                return;
            }

            var nodeType = nodeRegistry.Get(node.TypeName);
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

    private static TimeSpan CalculateBackoff(RetryPolicy? policy, int attempt, EngineDefaultsOptions? defaults = null)
    {
        var baseDelay = policy?.BaseDelay > TimeSpan.Zero
            ? policy.BaseDelay
            : TimeSpan.FromSeconds(defaults?.DefaultBaseDelaySeconds ?? 1);
        var maxDelay = policy?.MaxDelay > TimeSpan.Zero
            ? policy.MaxDelay
            : TimeSpan.FromSeconds(defaults?.DefaultMaxDelaySeconds ?? 60);

        var strategy = policy?.BackoffStrategy ?? BackoffStrategy.Exponential;

        TimeSpan delay = strategy switch
        {
            BackoffStrategy.Linear => baseDelay * (attempt + 1),
            BackoffStrategy.Fixed => baseDelay,
            _ => TimeSpan.FromTicks((long)(baseDelay.Ticks * Math.Pow(2, attempt))) // Exponential
        };

        delay = TimeSpan.FromTicks(Math.Min(delay.Ticks, maxDelay.Ticks));

        if (policy?.UseJitter == true)
        {
            var jitter = Random.Shared.NextDouble() * delay.TotalMilliseconds;
            delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds + jitter);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks, maxDelay.Ticks));
        }

        return delay;
    }

    // 端口名称直接从（已按当前节点参数水合的）INodeType 实例读取。
    // 注意：注册表返回的是按 TypeName 缓存的单例，执行器在处理每个节点时会用该节点的
    // 参数（含 SwitchNode.Cases）水合该单例，且节点按队列顺序串行执行+路由，因此此处读取到的
    // 即当前节点的端口。原先按 TypeName 缓存端口会导致不同 Cases 的 Switch 节点互相串扰（路由错位），
    // 故不再缓存，直接读取以兼得正确性与无界缓存风险消除。
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
        Dictionary<string, NodeDefinition> nodeMap,
        ILookup<(string SourceNodeId, string SourcePortName), Connection> connectionsBySource,
        ConcurrentDictionary<string, ILlmClient> nodeLlmClients)
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
                .Where(c => c.TargetNodeId == node.Id && c.TargetPortName is not null && c.TargetPortName.Equals(port.Name, StringComparison.OrdinalIgnoreCase))
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

    private NodeExecutionRecord BuildNodeExecutionRecord(
        string nodeDefinitionId,
        int runIndex,
        IReadOnlyDictionary<string, DataBatch> inputs,
        NodeExecutionResult output,
        NodeExecutionContext context,
        IReadOnlySet<string> sensitiveValues)
    {
        return new NodeExecutionRecord
        {
            Id = context.NodeExecutionRecordId,
            NodeDefinitionId = nodeDefinitionId,
            RunIndex = runIndex,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Inputs = inputs.ToDictionary(kv => kv.Key, kv => secretMasker.MaskDataBatch(kv.Value, sensitiveValues), StringComparer.OrdinalIgnoreCase),
            Output = secretMasker.MaskOutput(output, sensitiveValues),
            RawParameters = secretMasker.MaskParameters(context.RawParameters, sensitiveValues),
            ResolvedParameters = secretMasker.MaskParameters(context.ResolvedParameters, sensitiveValues)
        };
    }

    private NodeExecutionRecord BuildNodeExecutionRecord(
        string nodeDefinitionId,
        int runIndex,
        IReadOnlyDictionary<string, DataBatch> inputs,
        NodeExecutionResult output,
        IReadOnlyDictionary<string, object> rawParameters,
        IReadOnlyDictionary<string, object> resolvedParameters,
        IReadOnlySet<string> sensitiveValues)
    {
        return new NodeExecutionRecord
        {
            NodeDefinitionId = nodeDefinitionId,
            RunIndex = runIndex,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Inputs = inputs.ToDictionary(kv => kv.Key, kv => secretMasker.MaskDataBatch(kv.Value, sensitiveValues), StringComparer.OrdinalIgnoreCase),
            Output = secretMasker.MaskOutput(output, sensitiveValues),
            RawParameters = secretMasker.MaskParameters(rawParameters, sensitiveValues),
            ResolvedParameters = secretMasker.MaskParameters(resolvedParameters, sensitiveValues)
        };
    }
}
