using FlowEngine.Core.Enums;

namespace FlowEngine.Host.Webhooks;

/// <summary>
/// Webhook 同步模式完成通知（EX-4）：将同步 Webhook 的"等待执行完成"从 DB 轮询改为事件驱动。
/// <see cref="WebhookHandler"/> 在启动工作流后调用 <see cref="WaitAsync"/> 注册并异步等待；
/// 工作流完成时由 <see cref="WebhookCompletionNotifier"/> 经 <see cref="Complete"/> 唤醒对应 Task。
/// </summary>
public interface IWebhookSyncCompletionService
{
    /// <summary>
    /// 注册并等待指定执行完成。若执行在注册前已完成（竞态），立即返回最终状态。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="timeout">最长等待时间。</param>
    /// <param name="ct">取消令牌（客户端断开时触发）。</param>
    /// <returns>执行最终状态；超时或取消时抛出 <see cref="TimeoutException"/> / <see cref="OperationCanceledException"/>。</returns>
    Task<ExecutionStatus> WaitAsync(Guid executionId, TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// 通知指定执行已完成，唤醒正在等待的调用方。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="status">最终状态。</param>
    void Complete(Guid executionId, ExecutionStatus status);
}
