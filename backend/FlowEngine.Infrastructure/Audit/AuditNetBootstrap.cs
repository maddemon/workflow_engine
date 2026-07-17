using System.Threading;
using Audit.Core;

namespace FlowEngine.Infrastructure.Audit;

/// <summary>
/// Audit.NET 全局配置引导。
/// 确保 <see cref="FlowEngineAuditJsonAdapter"/> 仅被注册一次（Audit.NET 的序列化配置为进程级全局状态）。
/// <see cref="AuditLogFileSink"/> 与 <c>ServiceCollectionExtensions</c> 均调用此方法，
/// 使 Sink 在 DI 之外（如单元测试）被直接构造时也能获得正确的序列化行为。
/// </summary>
public static class AuditNetBootstrap
{
    private static int _initialized;

    /// <summary>
    /// 确保 Audit.NET 已配置为使用 <see cref="FlowEngineAuditJsonAdapter"/>。幂等。
    /// </summary>
    public static void EnsureConfigured()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        Configuration.Setup().JsonAdapter(new FlowEngineAuditJsonAdapter());
    }
}
