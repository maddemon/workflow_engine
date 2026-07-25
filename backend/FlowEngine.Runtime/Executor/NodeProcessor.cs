using System.Collections.Concurrent;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Security;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 单个节点的处理：从 <see cref="WorkflowSchedulerKernel"/> 抽离的单一职责协作者。
/// 负责按队列取出工作项、构造上下文、调用重试执行、累积输出、脱敏建记录、
/// 路由下游，并承载环路失控保护。返回 <c>true</c> 表示应终止调度（节点失败且非 Continue）。
/// </summary>
public sealed class NodeProcessor
{
    private readonly INodeRegistry _nodeRegistry;
    private readonly NodeExecutionContextFactory _contextFactory;
    private readonly SecretMasker _secretMasker;
    private readonly RetryExecutor _retryExecutor;
    private readonly OutputRouter _outputRouter;
    private readonly EngineDefaultsOptions _defaults;

    /// <summary>
    /// 构造节点处理器。
    /// </summary>
    /// <param name="nodeRegistry">节点注册中心。</param>
    /// <param name="contextFactory">节点执行上下文工厂。</param>
    /// <param name="secretMasker">敏感值脱敏器。</param>
    /// <param name="retryExecutor">重试执行器。</param>
    /// <param name="outputRouter">输出路由器。</param>
    /// <param name="defaults">引擎默认配置（环路上限、保留输出上限等）。</param>
    public NodeProcessor(
        INodeRegistry nodeRegistry,
        NodeExecutionContextFactory contextFactory,
        SecretMasker secretMasker,
        RetryExecutor retryExecutor,
        OutputRouter outputRouter,
        EngineDefaultsOptions defaults)
    {
        _nodeRegistry = nodeRegistry;
        _contextFactory = contextFactory;
        _secretMasker = secretMasker;
        _retryExecutor = retryExecutor;
        _outputRouter = outputRouter;
        _defaults = defaults;
    }

    /// <summary>
    /// 处理单个节点工作项：构造上下文、执行（带重试）、建记录、累积输出、路由下游。
    /// </summary>
    /// <param name="item">待处理工作项。</param>
    /// <param name="session">执行会话。</param>
    /// <param name="sideEffects">副作用回调（持久化 / 事件发布）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否应终止调度（节点失败且错误策略非 Continue）。</returns>
    public async Task<bool> ProcessAsync(
        NodeWorkItem item,
        ExecutionSession session,
        IExecutionSideEffects sideEffects,
        CancellationToken cancellationToken)
    {
        if (!session.NodeMap.TryGetValue(item.NodeInstanceId, out var node))
        {
            return false;
        }

        var nodeType = _nodeRegistry.Get(node.TypeName);
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
                await sideEffects.PublishNodeErrorAsync(session.Execution.Id, node.Id, 0, SchedulerHelpers.SafeError(limitError), cancellationToken).ConfigureAwait(false);
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
                context = await _contextFactory.CreateAsync(
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
                    Error = NodeErrorFactory.Sanitize(ex, "ScriptParameterPreEvaluationError", node.Id.ToString())
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
                await sideEffects.PublishNodeErrorAsync(session.Execution.Id, node.Id, runIndex, SchedulerHelpers.SafeError(failureResult.Error), cancellationToken).ConfigureAwait(false);

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

            // 记录节点实际执行开始时间；首个节点继承执行的 StartedAt 以包含引擎初始化开销。
            var nodeStartedAt = session.Execution.NodeRecords.Count == 0
                ? session.Execution.StartedAt
                : DateTime.UtcNow;

            NodeExecutionResult result;
            try
            {
                result = await _retryExecutor.ExecuteNodeWithRetryAsync(node, nodeType, context, cancellationToken)
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

            var record = BuildNodeExecutionRecord(node.Id, runIndex, runInputs, result, context, session.SensitiveValues, nodeStartedAt);

            session.Execution.NodeRecords.Add(record);
            await sideEffects.PersistNodeRecordAsync(record, cancellationToken).ConfigureAwait(false);

            // OBS-2：发布节点执行完成或错误事件（成功与失败均发布，供审计与实时推送消费）。
            if (result.Success)
            {
                await sideEffects.PublishNodeExecutedAsync(
                    session.Execution.Id, node.Id, runIndex, result, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await sideEffects.PublishNodeErrorAsync(
                    session.Execution.Id, node.Id, runIndex, SchedulerHelpers.SafeError(result.Error), cancellationToken).ConfigureAwait(false);
            }

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
        // 本次要路由给下游的完整批（CON-5）：在限流前捕获完整累积批引用，
        // 确保下游节点收到全量数据；限流仅作用于 SuccessfulOutputs / LatestBatches 中保留的历史快照，
        // 不会截断本次已路由给下游的数据。
        DataBatch routingBatch;
        if (accumulatedItems.Count > 0)
        {
            var cumulative = new DataBatch { Items = accumulatedItems.ToList() };
            session.LatestBatches[node.Name] = cumulative;
            routingBatch = cumulative;

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
            routingBatch = finalResult.Output;
        }

        // CON-5：限制单个节点在 SuccessfulOutputs / LatestBatches 中保留的输出项数，
        // 避免大批次（如大 OncePerItem 输入）在整个运行期间无界常驻内存。
        if (_defaults.MaxRetainedOutputItems > 0)
        {
            CapRetainedOutput(session, node.Name);
        }

        // OncePerItem：下游边消费的是累积批（全部项）而非最后一次运行的单批，避免静默丢数据。
        // finalResult 在上方 `if (finalResult is null) return false;` 后已确定非 null。
        var resultForRouting = accumulatedItems.Count > 0
            ? new NodeExecutionResult
            {
                Success = finalResult!.Success,
                Output = routingBatch,
                BranchIndex = finalResult!.BranchIndex,
                Error = finalResult!.Error,
                ToolExecutionRecords = finalResult!.ToolExecutionRecords,
            }
            : finalResult!;
        await _outputRouter.RouteOutputsAsync(node, nodeType, resultForRouting, session, sideEffects, cancellationToken).ConfigureAwait(false);

        return false;
    }

    /// <summary>
    /// 解析节点运行输入：非 OncePerItem 原样透传；OncePerItem 按 runIndex 取各端口第 runIndex 个数据项。
    /// </summary>
    /// <param name="inputs">原始按端口组织的输入批。</param>
    /// <param name="mode">节点执行模式。</param>
    /// <param name="runIndex">当前运行索引。</param>
    /// <returns>本次运行的输入。</returns>
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

    /// <summary>
    /// 限制单节点保留输出项数（CON-5）：超过上限时仅保留最新 N 项，作用于
    /// <see cref="ExecutionSession.SuccessfulOutputs"/> 与 <see cref="ExecutionSession.LatestBatches"/>。
    /// </summary>
    /// <param name="session">执行会话。</param>
    /// <param name="nodeName">节点名。</param>
    private void CapRetainedOutput(ExecutionSession session, string nodeName)
    {
        var max = _defaults.MaxRetainedOutputItems;
        if (session.SuccessfulOutputs.TryGetValue(nodeName, out var so) && so.Items.Count > max)
        {
            session.SuccessfulOutputs[nodeName] = Cap(so, max);
        }

        if (session.LatestBatches.TryGetValue(nodeName, out var lb) && lb.Items.Count > max)
        {
            session.LatestBatches[nodeName] = Cap(lb, max);
        }
    }

    /// <summary>
    /// 截断为最新 max 项（OncePerItem 按 SourceIndex 升序累积，末段即最近输出）。
    /// </summary>
    /// <param name="batch">原始批。</param>
    /// <param name="max">保留项数上限。</param>
    /// <returns>截断后的批。</returns>
    private static DataBatch Cap(DataBatch batch, int max)
    {
        // 保留最新 max 项（OncePerItem 按 SourceIndex 升序累积，末段即最近输出）。
        return new DataBatch { Items = batch.Items.Skip(batch.Items.Count - max).ToList() };
    }

    /// <summary>
    /// 为 LLM 类节点解析其上游供给的 LLM 客户端：遍历节点的 LLM 输入端口，
    /// 沿入边在 <see cref="ExecutionSession.NodeLlmClients"/> 中查找上游已注册的客户端。
    /// </summary>
    /// <param name="node">当前节点定义。</param>
    /// <param name="nodeType">当前节点类型实例。</param>
    /// <param name="nodeMap">节点映射。</param>
    /// <param name="connectionsBySource">按源端口分组的连接查找。</param>
    /// <param name="nodeLlmClients">节点 LLM 客户端注册表。</param>
    /// <returns>解析到的 LLM 客户端；无则返回 null。</returns>
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

    /// <summary>
    /// 由节点执行上下文构造节点执行记录，并对输入/输出/参数做敏感值脱敏。
    /// </summary>
    /// <param name="nodeDefinitionId">节点定义 ID。</param>
    /// <param name="runIndex">运行索引。</param>
    /// <param name="inputs">本次运行输入。</param>
    /// <param name="output">本次运行输出。</param>
    /// <param name="context">节点执行上下文（含脱敏所需的原始/已解析参数与记录 ID）。</param>
    /// <param name="sensitiveValues">敏感值集合（字面凭据）。</param>
    /// <param name="startedAt">节点执行开始时间。</param>
    /// <returns>脱敏后的节点执行记录。</returns>
    private NodeExecutionRecord BuildNodeExecutionRecord(
        string nodeDefinitionId,
        int runIndex,
        IReadOnlyDictionary<string, DataBatch> inputs,
        NodeExecutionResult output,
        NodeExecutionContext context,
        IReadOnlySet<string> sensitiveValues,
        DateTime startedAt)
    {
        return new NodeExecutionRecord
        {
            Id = context.NodeExecutionRecordId,
            NodeDefinitionId = nodeDefinitionId,
            RunIndex = runIndex,
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
            Inputs = inputs.ToDictionary(kv => kv.Key, kv => _secretMasker.MaskDataBatch(kv.Value, sensitiveValues), StringComparer.OrdinalIgnoreCase),
            Output = _secretMasker.MaskOutput(output, sensitiveValues),
            RawParameters = _secretMasker.MaskParameters(context.RawParameters, sensitiveValues),
            ResolvedParameters = _secretMasker.MaskParameters(context.ResolvedParameters, sensitiveValues)
        };
    }

    /// <summary>
    /// 由裸参数构造节点执行记录（超时等无上下文场景），并对输入/输出/参数做敏感值脱敏。
    /// </summary>
    /// <param name="nodeDefinitionId">节点定义 ID。</param>
    /// <param name="runIndex">运行索引。</param>
    /// <param name="inputs">本次运行输入。</param>
    /// <param name="output">本次运行输出。</param>
    /// <param name="rawParameters">原始参数。</param>
    /// <param name="resolvedParameters">已解析参数。</param>
    /// <param name="sensitiveValues">敏感值集合（字面凭据）。</param>
    /// <returns>脱敏后的节点执行记录。</returns>
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
            Inputs = inputs.ToDictionary(kv => kv.Key, kv => _secretMasker.MaskDataBatch(kv.Value, sensitiveValues), StringComparer.OrdinalIgnoreCase),
            Output = _secretMasker.MaskOutput(output, sensitiveValues),
            RawParameters = _secretMasker.MaskParameters(rawParameters, sensitiveValues),
            ResolvedParameters = _secretMasker.MaskParameters(resolvedParameters, sensitiveValues)
        };
    }
}
