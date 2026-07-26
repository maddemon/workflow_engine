using System.Collections.Generic;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Execution.Pipeline;
using FlowEngine.Runtime.Security;

namespace FlowEngine.Runtime.Execution.Stages;

/// <summary>
/// 执行阶段：承载原 <see cref="NodeProcessor.ProcessAsync"/> 的每次运行主体。逐次创建节点执行上下文、
/// 解析 LLM 客户端、托底执行（带重试）、释放 JS 引擎、脱敏建记录并逐次持久化、累积成功输出，
/// 并按原语义处理脚本错误与节点失败短路（环路上限 / 失败且错误策略非 Continue → 置
/// <see cref="NodePipelineContext.ShouldTerminateWorkflow"/> 并短路）。循环结束后构造路由批与
/// <see cref="NodePipelineContext.RoutingResult"/> 供下游 <see cref="RoutingStage"/> 消费。
/// 正常路径逐次记录持久化完成后置 <see cref="NodePipelineContext.ExecutedAndPersisted"/>，
/// 供末端 <see cref="PersistenceStage"/> 跳过重复持久化。
/// </summary>
public sealed class ExecutionStage(
    NodeExecutionContextFactory contextFactory,
    RetryExecutor retryExecutor,
    SecretMasker secretMasker,
    EngineDefaultsOptions defaults) : IExecutionStage
{
    /// <summary>执行节点主体。节点缺失（未初始化）或取消/无路由结果时提前返回且不调用 next；失败短路时设置 Result 并短路。</summary>
    /// <param name="context">管线上下文（由 <see cref="InitializeStage"/> 填充 NodeDefinition / NodeType / NodeContext / RunCount / ExecutionMode）。</param>
    /// <param name="next">后续阶段驱动委托。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task RunAsync(NodePipelineContext context, Func<Task> next, CancellationToken ct)
    {
        var session = context.Session;
        var sideEffects = context.SideEffects;
        var node = context.NodeDefinition!;
        var nodeType = context.NodeType!;
        var executionMode = context.ExecutionMode;
        var runCount = context.RunCount;
        var nodeContext = context.NodeContext!;

        NodeExecutionResult? finalResult = null;
        // 累积本节点本次调用的各次成功运行输出（OncePerItem 会按批次多次运行同一节点），
        // 全部项都需进入 SuccessfulOutputs / LatestBatches，供下游 $node.<name> / $items(<name>) 读取。
        var accumulatedItems = new List<DataItem>();

        for (var runIndex = 0; runIndex < runCount; runIndex++)
        {
            var runInputs = NodeExecutionHelpers.BuildRunInputs(context.Item.Inputs, executionMode, runIndex);
            NodeExecutionContext? nodeExecContext = null;
            try
            {
                nodeExecContext = await contextFactory.CreateAsync(
                    session.Workflow,
                    session.Execution,
                    node,
                    nodeType,
                    runInputs,
                    session.SuccessfulOutputs,
                    session.LatestBatches,
                    runIndex,
                    ct,
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
                await sideEffects.PublishNodeStartedAsync(session.Execution.Id, node.Id, runIndex, ct).ConfigureAwait(false);
                await sideEffects.PersistNodeRecordAsync(failedRecord, ct).ConfigureAwait(false);
                await sideEffects.PublishNodeErrorAsync(session.Execution.Id, node.Id, runIndex, SchedulerHelpers.SafeError(failureResult.Error), ct).ConfigureAwait(false);

                finalResult = failureResult;
                continue; // 跳到下一次 runIndex
            }

            nodeExecContext.NodeExecutionRecordId = Guid.NewGuid();
            nodeExecContext.Memory = session.Memory;

            var resolvedLlmClient = NodeExecutionHelpers.ResolveLlmClientForNode(node, nodeType, session.NodeMap, session.ConnectionsBySource, session.NodeLlmClients);
            if (resolvedLlmClient is not null)
            {
                nodeExecContext.LlmClient = resolvedLlmClient;
            }

            nodeExecContext.OnLlmStreamChunk = sideEffects.CreateLlmStreamCallback(session.Execution.Id, node.Id, runIndex);

            await sideEffects.PublishNodeStartedAsync(session.Execution.Id, node.Id, runIndex, ct)
                .ConfigureAwait(false);

            // 记录节点实际执行开始时间；首个节点继承执行的 StartedAt 以包含引擎初始化开销。
            var nodeStartedAt = session.Execution.NodeRecords.Count == 0
                ? session.Execution.StartedAt
                : DateTime.UtcNow;

            NodeExecutionResult result;
            try
            {
                result = await retryExecutor.ExecuteNodeWithRetryAsync(node, nodeType, nodeExecContext, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                // 释放节点执行期间托管的 JS 引擎（含重试循环结束后统一释放）。
                nodeExecContext.ReleaseEngine();
            }

            if (nodeExecContext.LlmClient is not null)
            {
                session.NodeLlmClients[node.Id] = nodeExecContext.LlmClient;
            }

            var record = NodeExecutionHelpers.BuildNodeExecutionRecord(
                node.Id, runIndex, runInputs, result, nodeExecContext, session.SensitiveValues, nodeStartedAt, secretMasker);

            session.Execution.NodeRecords.Add(record);
            await sideEffects.PersistNodeRecordAsync(record, ct).ConfigureAwait(false);

            // 发布节点执行完成或错误事件（成功与失败均发布，供审计与实时推送消费）。
            if (result.Success)
            {
                await sideEffects.PublishNodeExecutedAsync(
                    session.Execution.Id, node.Id, runIndex, result, ct).ConfigureAwait(false);
            }
            else
            {
                await sideEffects.PublishNodeErrorAsync(
                    session.Execution.Id, node.Id, runIndex, SchedulerHelpers.SafeError(result.Error), ct).ConfigureAwait(false);
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
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                session.Execution.Status = ExecutionStatus.Failed;
                session.Execution.CompletedAt = DateTime.UtcNow;
                await sideEffects.PersistFailedStateAsync(ct).ConfigureAwait(false);
                session.WaitingArea.CleanupExecution(session.Execution.Id);
                context.ShouldTerminateWorkflow = true;
                context.Result = result;
                return; // 短路，不调用 next。
            }
        }

        // 正常路径：逐次记录已持久化完成，置标记供末端阶段跳过重复持久化。
        context.ExecutedAndPersisted = true;

        if (finalResult is null)
        {
            // 无任何运行（理论上不会发生，runCount≥1）：不路由，不做短路。
            context.RoutingResult = null;
            return;
        }

        // 已请求取消：不再路由输出、不再覆写状态，交由 RunAsync 外层统一落库 Cancelled。
        if (ct.IsCancellationRequested)
        {
            context.RoutingResult = null;
            return;
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
            // 合法输出会被静默丢弃。
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
        if (defaults.MaxRetainedOutputItems > 0)
        {
            NodeExecutionHelpers.CapRetainedOutput(session, node.Name, defaults.MaxRetainedOutputItems);
        }

        // OncePerItem：下游边消费的是累积批（全部项）而非最后一次运行的单批，避免静默丢数据。
        var resultForRouting = accumulatedItems.Count > 0
            ? new NodeExecutionResult
            {
                Success = finalResult.Success,
                Output = routingBatch,
                BranchIndex = finalResult.BranchIndex,
                Error = finalResult.Error,
                ToolExecutionRecords = finalResult.ToolExecutionRecords,
            }
            : finalResult;

        context.RoutingResult = resultForRouting;
        await next();
    }
}
