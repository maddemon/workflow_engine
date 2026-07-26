using FlowEngine.Runtime.Execution.Pipeline;

namespace FlowEngine.Runtime.Execution.Stages;

/// <summary>
/// 后处理阶段（Phase 2 透传占位）：累积与保留输出限流已在 <see cref="ExecutionStage"/> 内完成。
/// Phase 3 可在此承载速率限制、输出归一化等横切后处理；当前仅驱动后续阶段。
/// </summary>
public sealed class PostProcessStage : IExecutionStage
{
    /// <summary>透传：直接驱动后续阶段。</summary>
    /// <param name="context">管线上下文。</param>
    /// <param name="next">后续阶段驱动委托。</param>
    /// <param name="ct">取消令牌。</param>
    public Task RunAsync(NodePipelineContext context, Func<Task> next, CancellationToken ct) => next();
}
