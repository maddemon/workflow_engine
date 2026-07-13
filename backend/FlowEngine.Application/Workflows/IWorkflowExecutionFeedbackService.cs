using FlowEngine.Application.Dtos;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 执行反馈服务接口——分析执行结果，提供结构化反馈以支持 AI 自动修复。
/// 抽离为接口以便依赖注入与单元测试（底层实现为 <see cref="WorkflowExecutionFeedbackService"/>）。
/// </summary>
public interface IWorkflowExecutionFeedbackService
{
    /// <summary>
    /// 获取执行反馈。
    /// </summary>
    /// <param name="executionId">执行记录 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行反馈结果；执行不存在时返回 null。</returns>
    Task<ExecutionFeedbackResult?> GetFeedbackAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);
}
