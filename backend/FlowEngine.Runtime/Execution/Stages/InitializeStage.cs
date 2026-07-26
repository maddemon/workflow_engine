using System.Collections.Generic;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Execution.Pipeline;

namespace FlowEngine.Runtime.Execution.Stages;

/// <summary>
/// 初始化阶段：从会话节点映射解析节点定义与节点类型，计算运行次数，管理节点上下文生命周期，
/// 并执行环路失控保护（反馈边激活累计超过上限 → 判定为无限回环，转 Failed）。
/// 节点缺失时直接短路（不调用 next），由驱动器返回中性结果（shouldStop=false）。
/// </summary>
public sealed class InitializeStage(INodeRegistry nodeRegistry, EngineDefaultsOptions defaults) : IExecutionStage
{
    /// <summary>执行初始化。节点缺失时返回且不调用 next；环路上限触发时设置
    /// <see cref="NodePipelineContext.ShouldTerminateWorkflow"/> 与 <see cref="NodePipelineContext.Result"/> 并短路。</summary>
    /// <param name="context">管线上下文，输出 NodeDefinition / NodeType / NodeContext / RunCount / ExecutionMode。</param>
    /// <param name="next">后续阶段驱动委托。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task RunAsync(NodePipelineContext context, Func<Task> next, CancellationToken ct)
    {
        var session = context.Session;
        var sideEffects = context.SideEffects;

        if (!session.NodeMap.TryGetValue(context.Item.NodeInstanceId, out var node))
        {
            // 节点缺失：不调用 next，不设置 Result → 驱动器返回中性结果，shouldStop=false。
            return;
        }

        var nodeType = nodeRegistry.Get(node.TypeName);
        var executionMode = nodeType.ExecutionMode;
        var runCount = executionMode == ExecutionMode.OncePerItem
            ? Math.Max(1, context.Item.Inputs.Values.DefaultIfEmpty(new DataBatch()).Max(b => b.Items.Count))
            : 1;

        // 节点上下文生命周期：非回边激活（新上游输入）→ 清空旧上下文与反馈计数；
        // 回边激活（环路继续）→ 保留上下文，复用既有迭代状态（LoopNode 正常循环依赖此路径）。
        if (!context.Item.IsFeedbackActivation)
        {
            session.NodeContexts.TryRemove(node.Id, out _);
            // 新上游输入开启新一轮循环，重置反馈激活计数（见下方环路失控保护）。
            session.FeedbackActivationCounts.TryRemove(node.Id, out _);
        }
        else
        {
            // 环路失控保护：反馈边激活累计超过上限 → 判定为无限回环，转 Failed。
            var feedbackCount = session.FeedbackActivationCounts.AddOrUpdate(node.Id, 1, (_, v) => v + 1);
            if (defaults.MaxCycleIterations > 0 && feedbackCount > defaults.MaxCycleIterations)
            {
                var limitError = new NodeError
                {
                    Code = "CycleLimitExceeded",
                    Message = $"节点 {node.Name} ({node.Id}) 反馈边激活次数达 {feedbackCount}，超过上限 {defaults.MaxCycleIterations}，判定为环路失控。",
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
                await sideEffects.PersistNodeRecordAsync(limitRecord, ct).ConfigureAwait(false);
                await sideEffects.PublishNodeErrorAsync(session.Execution.Id, node.Id, 0, SchedulerHelpers.SafeError(limitError), ct).ConfigureAwait(false);
                session.Execution.Status = ExecutionStatus.Failed;
                session.Execution.CompletedAt = DateTime.UtcNow;
                await sideEffects.PersistFailedStateAsync(ct).ConfigureAwait(false);
                session.WaitingArea.CleanupExecution(session.Execution.Id);
                context.ShouldTerminateWorkflow = true;
                context.Result = limitRecord.Output;
                return; // 短路，不调用 next。
            }
        }

        var nodeContext = session.NodeContexts.GetOrAdd(
            node.Id,
            _ => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));

        context.NodeContext = nodeContext;
        context.NodeDefinition = node;
        context.NodeType = nodeType;
        context.ExecutionMode = executionMode;
        context.RunCount = runCount;

        await next();
    }
}
