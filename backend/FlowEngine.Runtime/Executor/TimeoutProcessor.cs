using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Security;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 等待区超时处理：从 <see cref="WorkflowSchedulerKernel"/> 抽离的单一职责协作者。
/// 扫描等待区超时项，对非 Continue 策略节点转 Failed 并终止；对 Continue 策略节点
/// 经错误策略处理后路由下游（复用 <see cref="OutputRouter"/>）。
/// </summary>
public sealed class TimeoutProcessor
{
    private readonly INodeRegistry _nodeRegistry;
    private readonly ErrorStrategyHandler _errorHandler;
    private readonly SecretMasker _secretMasker;
    private readonly OutputRouter _outputRouter;

    /// <summary>
    /// 构造超时处理器。
    /// </summary>
    /// <param name="nodeRegistry">节点注册中心。</param>
    /// <param name="errorHandler">错误策略处理。</param>
    /// <param name="secretMasker">敏感值脱敏器。</param>
    /// <param name="outputRouter">输出路由器（Continue 策略下路由下游）。</param>
    public TimeoutProcessor(
        INodeRegistry nodeRegistry,
        ErrorStrategyHandler errorHandler,
        SecretMasker secretMasker,
        OutputRouter outputRouter)
    {
        _nodeRegistry = nodeRegistry;
        _errorHandler = errorHandler;
        _secretMasker = secretMasker;
        _outputRouter = outputRouter;
    }

    /// <summary>
    /// 处理等待区中已超时的节点：构造超时失败记录并持久化/发布；非 Continue 策略转 Failed 并终止，
    /// Continue 策略经错误策略处理后路由下游。
    /// </summary>
    /// <param name="session">执行会话（等待区 / 队列）。</param>
    /// <param name="sideEffects">副作用回调（持久化 / 事件发布）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task ProcessAsync(
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
                resolvedParameters: new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase),
                sensitiveValues: session.SensitiveValues);

            session.Execution.NodeRecords.Add(record);
            await sideEffects.PersistNodeRecordAsync(record, cancellationToken).ConfigureAwait(false);
            await sideEffects.PublishNodeErrorAsync(session.Execution.Id, node.Id, 0, SchedulerHelpers.SafeError(timeoutResult.Error), cancellationToken).ConfigureAwait(false);

            if (node.ErrorStrategy != ErrorStrategy.Continue)
            {
                session.Execution.Status = ExecutionStatus.Failed;
                session.Execution.CompletedAt = DateTime.UtcNow;
                await sideEffects.PersistFailedStateAsync(cancellationToken).ConfigureAwait(false);
                session.WaitingArea.CleanupExecution(session.Execution.Id);
                return;
            }

            var nodeType = _nodeRegistry.Get(node.TypeName);
            await _outputRouter.RouteOutputsAsync(node, nodeType, timeoutResult, session, sideEffects, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 由裸参数构造节点执行记录（超时场景，无执行上下文），并对输入/输出/参数做敏感值脱敏。
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
