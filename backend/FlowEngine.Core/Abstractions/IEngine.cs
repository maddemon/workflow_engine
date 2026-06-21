using FlowEngine.Core.ValueObjects;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 工作流执行引擎。
/// </summary>
public interface IEngine
{
    /// <summary>
    /// 启动指定工作流。
    /// </summary>
    /// <param name="workflowDefinitionId">工作流定义 ID。</param>
    /// <param name="triggerPayload">触发负载。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行 ID。</returns>
    Task<ExecutionId> StartAsync(
        Guid workflowDefinitionId,
        object? triggerPayload = null,
        CancellationToken cancellationToken = default);
}
