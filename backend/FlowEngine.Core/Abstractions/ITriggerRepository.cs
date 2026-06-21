using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 触发器仓储。
/// </summary>
public interface ITriggerRepository
{
    /// <summary>
    /// 按 ID 获取触发器。
    /// </summary>
    Task<Trigger?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按工作流定义 ID 获取触发器列表。
    /// </summary>
    Task<IReadOnlyCollection<Trigger>> GetByWorkflowDefinitionIdAsync(
        Guid workflowDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有激活的触发器。
    /// </summary>
    Task<IReadOnlyCollection<Trigger>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存触发器（新增或更新）。
    /// </summary>
    Task SaveAsync(Trigger trigger, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除触发器。
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按工作流定义 ID 删除所有触发器。
    /// </summary>
    Task DeleteByWorkflowDefinitionIdAsync(Guid workflowDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按工作流定义 ID 获取 Webhook 路由列表。
    /// </summary>
    Task<IReadOnlyCollection<WebhookRoute>> GetWebhookRoutesByWorkflowDefinitionIdAsync(
        Guid workflowDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按路径获取 Webhook 路由。
    /// </summary>
    Task<WebhookRoute?> GetWebhookRouteByPathAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有 Webhook 路由。
    /// </summary>
    Task<IReadOnlyCollection<WebhookRoute>> GetAllWebhookRoutesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存 Webhook 路由。
    /// </summary>
    Task SaveWebhookRouteAsync(WebhookRoute route, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除 Webhook 路由。
    /// </summary>
    Task DeleteWebhookRouteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按工作流定义 ID 删除所有 Webhook 路由。
    /// </summary>
    Task DeleteWebhookRoutesByWorkflowDefinitionIdAsync(Guid workflowDefinitionId, CancellationToken cancellationToken = default);
}
