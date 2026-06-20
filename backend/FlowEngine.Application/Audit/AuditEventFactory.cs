using FlowEngine.Application.Identity;
using FlowEngine.Core.Events;

namespace FlowEngine.Application.Audit;

/// <summary>
/// 审计事件工厂，根据当前用户上下文创建审计事件。
/// </summary>
public sealed class AuditEventFactory(IUserContext userContext)
{
    /// <summary>
    /// 创建审计事件，自动填充当前用户信息。
    /// </summary>
    public T Create<T>(
        string eventType,
        string resourceType,
        Guid resourceId,
        Dictionary<string, object>? payload = null,
        Dictionary<string, string>? metadata = null)
        where T : AuditEvent, new()
    {
        return new T
        {
            EventType = eventType,
            Actor = userContext.UserId?.ToString() ?? "system",
            ResourceType = resourceType,
            ResourceId = resourceId,
            Payload = payload,
            Metadata = metadata,
        };
    }
}
