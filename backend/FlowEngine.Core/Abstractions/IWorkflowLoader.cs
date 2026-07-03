using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 从持久化存储加载工作流定义。
/// </summary>
public interface IWorkflowLoader
{
    /// <summary>
    /// 按 ID 加载工作流。
    /// </summary>
    /// <param name="workflowId">工作流 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工作流实体；不存在时返回 null。</returns>
    Task<Workflow?> LoadAsync(Guid workflowId, CancellationToken cancellationToken = default);
}
