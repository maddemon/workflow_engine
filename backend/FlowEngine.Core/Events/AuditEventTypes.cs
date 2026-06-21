namespace FlowEngine.Core.Events;

/// <summary>
/// 审计事件类型常量。
/// </summary>
public static class AuditEventTypes
{
    /// <summary>工作流创建。</summary>
    public const string WorkflowCreated = "Workflow.Created";

    /// <summary>工作流更新。</summary>
    public const string WorkflowUpdated = "Workflow.Updated";

    /// <summary>工作流删除。</summary>
    public const string WorkflowDeleted = "Workflow.Deleted";

    /// <summary>工作流激活。</summary>
    public const string WorkflowActivated = "Workflow.Activated";

    /// <summary>工作流停用。</summary>
    public const string WorkflowDeactivated = "Workflow.Deactivated";

    /// <summary>执行开始。</summary>
    public const string ExecutionStarted = "Execution.Started";

    /// <summary>执行完成。</summary>
    public const string ExecutionCompleted = "Execution.Completed";

    /// <summary>执行失败。</summary>
    public const string ExecutionFailed = "Execution.Failed";

    /// <summary>执行取消。</summary>
    public const string ExecutionCancelled = "Execution.Cancelled";

    /// <summary>节点执行完成。</summary>
    public const string NodeExecuted = "Node.Executed";

    /// <summary>节点执行错误。</summary>
    public const string NodeError = "Node.Error";

    /// <summary>用户登录。</summary>
    public const string UserLogin = "User.Login";

    /// <summary>用户登出。</summary>
    public const string UserLogout = "User.Logout";

    /// <summary>用户注册。</summary>
    public const string UserRegistered = "User.Registered";

    /// <summary>凭据创建。</summary>
    public const string CredentialCreated = "Credential.Created";

    /// <summary>凭据访问。</summary>
    public const string CredentialAccessed = "Credential.Accessed";

    /// <summary>凭据删除。</summary>
    public const string CredentialDeleted = "Credential.Deleted";

    /// <summary>触发器创建。</summary>
    public const string TriggerCreated = "Trigger.Created";

    /// <summary>触发器更新。</summary>
    public const string TriggerUpdated = "Trigger.Updated";

    /// <summary>触发器删除。</summary>
    public const string TriggerDeleted = "Trigger.Deleted";

    /// <summary>Webhook 触发。</summary>
    public const string WebhookTriggered = "Webhook.Triggered";

    /// <summary>
    /// 需要同步刷盘的关键事件类型集合。
    /// </summary>
    public static readonly HashSet<string> CriticalEvents = new(StringComparer.Ordinal)
    {
        CredentialAccessed,
        CredentialDeleted,
        ExecutionCancelled,
    };
}
