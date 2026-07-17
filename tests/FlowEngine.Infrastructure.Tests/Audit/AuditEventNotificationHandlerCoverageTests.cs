using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FlowEngine.Application.Audit;
using FlowEngine.Core.Events;
using FlowEngine.Infrastructure.Audit;
using MediatR;
using Xunit;

namespace FlowEngine.Infrastructure.Tests.Audit;

/// <summary>
/// 健壮性测试：通过反射确保每一个 <see cref="AuditEvent"/> 子类型都被
/// <see cref="AuditEventNotificationHandler"/> 注册了对应的 MediatR 通知处理器。
/// MediatR 按事件的精确运行时类型分派，基类订阅无法被自动继承，因此新增审计事件子类型时必须显式补充处理器；
/// 本测试动态枚举所有子类型，一旦遗漏处理器即失败，避免静默漏写。
/// </summary>
public sealed class AuditEventNotificationHandlerCoverageTests
{
    private static readonly Type AuditEventBaseType = typeof(AuditEvent);
    private static readonly Type NotificationHandlerOpenType = typeof(INotificationHandler<>);

    /// <summary>
    /// <see cref="AuditEventNotificationHandler"/> 已实现处理器的事件类型集合（由反射动态得出，不硬编码）。
    /// </summary>
    private static readonly HashSet<Type> HandledEventTypes = GetHandledEventTypes();

    private static HashSet<Type> GetHandledEventTypes()
    {
        var handlerType = typeof(AuditEventNotificationHandler);
        var set = new HashSet<Type>();
        foreach (var iface in handlerType.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == NotificationHandlerOpenType)
            {
                set.Add(iface.GetGenericArguments()[0]);
            }
        }

        return set;
    }

    /// <summary>
    /// 动态枚举所有非抽象 <see cref="AuditEvent"/> 子类型。
    /// 覆盖实际承载子类型的 Core 与 Application 程序集；新增子类型只要落在这两个程序集中，
    /// 即会被自动纳入，无需改动本测试。注意：仅包含派生自 <see cref="AuditEvent"/> 的类型，
    /// 那些仅实现 <see cref="INotification"/> 但非审计事件契约的类型（如 <c>LlmTokenStreamEvent</c>）被正确排除。
    /// </summary>
    public static IEnumerable<object[]> AuditEventSubtypes()
    {
        var assemblies = new[] { typeof(AuditEvent).Assembly, typeof(AuditLogEvent).Assembly };
        return assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsAssignableTo(AuditEventBaseType) && !t.IsAbstract)
            .OrderBy(t => t.FullName)
            .Select(t => new object[] { t });
    }

    [Fact]
    public void AuditEventNotificationHandler_AtLeastOneAuditEventSubtype_Exists()
    {
        // 防止枚举为空时 Theory 静默通过（零数据视为通过）。
        Assert.NotEmpty(AuditEventSubtypes());
    }

    [Theory]
    [MemberData(nameof(AuditEventSubtypes))]
    public void AuditEventNotificationHandler_AllAuditEventSubtypes_HasRegisteredHandler(Type eventType)
    {
        Assert.Contains(eventType, HandledEventTypes);
    }
}
