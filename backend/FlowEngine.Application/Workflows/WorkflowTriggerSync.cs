using FlowEngine.Application.Authorization;
using FlowEngine.Application.Triggers;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Events;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 同步工作流激活状态变更的触发器注册，并发布相应审计事件。
/// </summary>
public sealed class WorkflowTriggerSync(
    TriggerService triggerService,
    AuthorizedOperationHandler handler)
{
    /// <summary>
    /// 根据工作流激活状态的前后变化，注册或注销触发器，并发布 Activated/Deactivated 审计事件。
    /// 仅在状态发生翻转时触发副作用。
    /// </summary>
    /// <param name="workflow">工作流实体（取 Id 与 Name 用于审计载荷）。</param>
    /// <param name="previousIsActive">变更前的激活状态。</param>
    /// <param name="currentIsActive">变更后的激活状态。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task SyncActivationAsync(
        Workflow workflow,
        bool previousIsActive,
        bool currentIsActive,
        CancellationToken ct)
    {
        if (previousIsActive && !currentIsActive)
        {
            await UnregisterTriggersAsync(workflow.Id, ct).ConfigureAwait(false);
            await handler.PublishAuditAsync(
                AuditEventTypes.WorkflowDeactivated,
                "Workflow",
                workflow.Id,
                new Dictionary<string, object> { ["name"] = workflow.Name },
                ct).ConfigureAwait(false);
        }
        else if (!previousIsActive && currentIsActive)
        {
            await RegisterTriggersAsync(workflow.Id, ct).ConfigureAwait(false);
            await handler.PublishAuditAsync(
                AuditEventTypes.WorkflowActivated,
                "Workflow",
                workflow.Id,
                new Dictionary<string, object> { ["name"] = workflow.Name },
                ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 注册工作流的触发器调度。
    /// </summary>
    public async Task RegisterTriggersAsync(Guid workflowDefinitionId, CancellationToken ct)
    {
        await triggerService.RegisterWorkflowSchedulesAsync(workflowDefinitionId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 注销工作流的触发器调度。
    /// </summary>
    public async Task UnregisterTriggersAsync(Guid workflowDefinitionId, CancellationToken ct)
    {
        await triggerService.UnregisterWorkflowSchedulesAsync(workflowDefinitionId, ct).ConfigureAwait(false);
    }
}
