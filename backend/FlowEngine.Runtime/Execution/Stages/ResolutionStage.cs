using FlowEngine.Runtime.Execution.Pipeline;

namespace FlowEngine.Runtime.Execution.Stages;

/// <summary>
/// 参数求值阶段。Phase 2 占位：<see cref="NodeExecutionContextFactory.CreateAsync"/> 已完成
/// Script 预求值，LLM 客户端解析保留在执行阶段内（保持行为一致）。NodeBase 节点的能力注入已迁移至
/// <see cref="ExecutionStage"/> 经 <see cref="NodeCapabilityInjector"/> 完成（god-object 晚于本阶段创建，
/// 故无法在此注入运行上下文能力），本阶段仅作透传。
/// </summary>
public sealed class ResolutionStage : IExecutionStage
{
    /// <summary>运行解析阶段：透传驱动后续阶段。</summary>
    /// <param name="context">管线上下文。</param>
    /// <param name="next">后续阶段驱动委托。</param>
    /// <param name="ct">取消令牌。</param>
    public Task RunAsync(NodePipelineContext context, Func<Task> next, CancellationToken ct)
    {
        return next();
    }
}
