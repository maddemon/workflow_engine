using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core.Entities;
using FlowEngine.Runtime.Executor;

namespace FlowEngine.Runtime.Execution.Pipeline;

/// <summary>节点执行管线：按固定有序阶段列表驱动单次节点执行。驱动器显式处理短路（校验/装配失败直达末端持久化阶段），不套用 ASP.NET 中间件 next 抽象。</summary>
public sealed class NodePipeline
{
    private readonly IReadOnlyList<IExecutionStage> _stages;

    /// <summary>以有序阶段列表构造管线；列表最后一个阶段被视为末端（持久化）阶段。</summary>
    /// <param name="stages">按执行顺序排列的阶段。</param>
    public NodePipeline(IEnumerable<IExecutionStage> stages) => _stages = [.. stages];

    /// <summary>运行管线并返回节点执行结果（内部构造上下文）。</summary>
    /// <param name="item">本次待执行的工作项。</param>
    /// <param name="session">执行会话。</param>
    /// <param name="sideEffects">执行副作用回调。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>节点执行结果；若没有任何阶段设置 Result，则返回默认成功空批次结果。</returns>
    public async Task<NodeExecutionResult> RunAsync(NodeWorkItem item, ExecutionSession session, IExecutionSideEffects sideEffects, CancellationToken ct)
    {
        var context = new NodePipelineContext(item, session, sideEffects);
        return await RunAsync(context, ct).ConfigureAwait(false);
    }

    /// <summary>使用调用方预构建的上下文运行管线（供编排者读取短路/终止等状态）。</summary>
    /// <param name="context">预构建的管线上下文（已注入 Item/Session/SideEffects）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>节点执行结果；若没有任何阶段设置 Result，则返回默认成功空批次结果。</returns>
    public async Task<NodeExecutionResult> RunAsync(NodePipelineContext context, CancellationToken ct)
    {
        await RunStagesFromAsync(0, context, ct).ConfigureAwait(false);
        return context.Result ?? new NodeExecutionResult { Success = true, Output = new DataBatch() };
    }

    private async Task RunStagesFromAsync(int start, NodePipelineContext context, CancellationToken ct)
    {
        // 短路标志：末端（持久化）阶段一旦执行（无论经正常链还是经短路跳转），即标记，
        // 避免任何路径下被重复执行（含“既设置 Result 又调用 next”的违规阶段）。
        var terminalRun = false;
        await RunCoreAsync(start);

        async Task RunCoreAsync(int index)
        {
            if (index >= _stages.Count)
            {
                return;
            }

            var isLast = index == _stages.Count - 1;

            // 末端阶段已执行过则跳过，避免重复。
            if (isLast && terminalRun)
            {
                return;
            }

            // next 驱动后续阶段：调用即继续，不调用即中断当前链（须由上层决定是否短路）。
            await _stages[index].RunAsync(context, () => RunCoreAsync(index + 1), ct).ConfigureAwait(false);

            if (context.Result is not null && !isLast)
            {
                // 非末端阶段设置 Result 且未调用 next → 短路直达末端持久化阶段，且只执行一次。
                if (!terminalRun)
                {
                    terminalRun = true;
                    await _stages[^1].RunAsync(context, () => Task.CompletedTask, ct).ConfigureAwait(false);
                }
            }
            else if (isLast)
            {
                // 记录末端已自然执行，防止后续（违规）短路再次触发末端。
                terminalRun = true;
            }
        }
    }
}
