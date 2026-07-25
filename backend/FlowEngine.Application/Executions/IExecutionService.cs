using FlowEngine.Application.Dtos;

namespace FlowEngine.Application.Executions;

/// <summary>
/// 执行服务接口。
/// </summary>
public interface IExecutionService
{
    /// <summary>
    /// 启动工作流执行。
    /// </summary>
    Task<ExecutionDto?> ExecuteAsync(
        Guid workflowId,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default,
        Dictionary<string, object>? inputs = null);

    /// <summary>
    /// 按 ID 获取执行详情（状态、输出、错误信息）。
    /// </summary>
    Task<ExecutionDto?> GetAsync(Guid executionId, CancellationToken cancellationToken = default);
}
