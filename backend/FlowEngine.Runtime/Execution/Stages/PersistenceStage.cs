using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Execution.Pipeline;
using FlowEngine.Runtime.Security;

namespace FlowEngine.Runtime.Execution.Stages;

/// <summary>
/// 末端（持久化）阶段：保证恰好执行一次并落库本次节点执行结果。
/// <list type="bullet">
///   <item>正常路径：<see cref="ExecutionStage"/> 已逐次持久化并置 <see cref="NodePipelineContext.ExecutedAndPersisted"/>，本阶段跳过。</item>
///   <item>环路上限短路：<see cref="InitializeStage"/> 已落库并置 <see cref="NodePipelineContext.ShouldTerminateWorkflow"/>，本阶段跳过。</item>
///   <item>校验/初始化等早期短路：<see cref="NodePipelineContext.Result"/> 已设置但尚末落库（<see cref="NodeExecutionContextFactory"/> 失败等）→ 构造失败记录并发布， behave 与真实节点失败一致。</item>
/// </list>
/// 当该早期失败且错误策略非 Continue 时，复刻 <see cref="NodeProcessor"/> 的失败终止副作用
/// （执行转 Failed、清理等待区、置 <see cref="NodePipelineContext.ShouldTerminateWorkflow"/>），使调度器正确终止。
/// </summary>
public sealed class PersistenceStage(SecretMasker secretMasker) : IExecutionStage
{
    /// <summary>末端阶段：按上述短路/正常判定决定是否补持久化早期失败结果。</summary>
    /// <param name="context">管线上下文。</param>
    /// <param name="next">终结委托（通常为 Task.CompletedTask）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task RunAsync(NodePipelineContext context, Func<Task> next, CancellationToken ct)
    {
        // 正常路径逐次记录已由 ExecutionStage 落库。
        if (context.ExecutedAndPersisted)
        {
            return;
        }

        // 环路上限短路：失败状态与记录已由 InitializeStage 落库。
        if (context.ShouldTerminateWorkflow)
        {
            return;
        }

        // 早期短路（校验失败等）且存在未落库的结果：补持久化，使行为等价于真实节点失败。
        if (context.Result is not null)
        {
            var node = context.NodeDefinition!;
            var session = context.Session;

            var record = new NodeExecutionRecord
            {
                Id = Guid.NewGuid(),
                NodeDefinitionId = node.Id,
                RunIndex = 0,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
                Inputs = context.Item.Inputs.ToDictionary(
                    kv => kv.Key, kv => secretMasker.MaskDataBatch(kv.Value, session.SensitiveValues), StringComparer.OrdinalIgnoreCase),
                Output = secretMasker.MaskOutput(context.Result, session.SensitiveValues),
                RawParameters = secretMasker.MaskParameters(new Dictionary<string, object>(), session.SensitiveValues),
                ResolvedParameters = secretMasker.MaskParameters(new Dictionary<string, object>(), session.SensitiveValues),
            };

            session.Execution.NodeRecords.Add(record);
            await context.SideEffects.PersistNodeRecordAsync(record, ct).ConfigureAwait(false);
            await context.SideEffects.PublishNodeStartedAsync(session.Execution.Id, node.Id, 0, ct).ConfigureAwait(false);
            await context.SideEffects.PublishNodeErrorAsync(
                session.Execution.Id, node.Id, 0, SchedulerHelpers.SafeError(context.Result.Error), ct).ConfigureAwait(false);

            // 终止型错误策略：复刻 NodeProcessor 的失败终止副作用，确保调度器返回 true 终止。
            if (node.ErrorStrategy != ErrorStrategy.Continue)
            {
                session.Execution.Status = ExecutionStatus.Failed;
                session.Execution.CompletedAt = DateTime.UtcNow;
                await context.SideEffects.PersistFailedStateAsync(ct).ConfigureAwait(false);
                session.WaitingArea.CleanupExecution(session.Execution.Id);
                context.ShouldTerminateWorkflow = true;
            }
        }
    }
}
