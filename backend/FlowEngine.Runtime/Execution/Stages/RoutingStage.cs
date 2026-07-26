using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Execution.Pipeline;

namespace FlowEngine.Runtime.Execution.Stages;

/// <summary>
/// 路由阶段：若 <see cref="ExecutionStage"/> 已构造路由结果（<see cref="NodePipelineContext.RoutingResult"/>），
/// 则调用 <see cref="OutputRouter.RouteOutputsAsync"/> 将节点输出分发至下游节点工作项。
/// 无路由结果（取消 / 短路 / 无运行）时跳过路由，仍驱动末端阶段。
/// </summary>
public sealed class RoutingStage(OutputRouter outputRouter) : IExecutionStage
{
    /// <summary>路由输出至下游。仅当存在路由结果且节点定义/类型齐备时调用路由器。</summary>
    /// <param name="context">管线上下文。</param>
    /// <param name="next">后续阶段驱动委托。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task RunAsync(NodePipelineContext context, Func<Task> next, CancellationToken ct)
    {
        if (context.RoutingResult is not null && context.NodeDefinition is not null && context.NodeType is not null)
        {
            await outputRouter.RouteOutputsAsync(
                context.NodeDefinition, context.NodeType, context.RoutingResult, context.Session, context.SideEffects, ct).ConfigureAwait(false);
        }

        await next();
    }
}
