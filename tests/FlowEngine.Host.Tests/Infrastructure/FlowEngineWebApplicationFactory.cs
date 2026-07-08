using System;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FlowEngine.Host.Tests.Infrastructure;

/// <summary>
/// 集成测试专用的 <see cref="WebApplicationFactory{TEntryPoint}"/> 工厂。
/// 在类型首次使用时设置默认管理员密码环境变量，确保空数据库首次启动时不因密码缺失而崩溃。
/// </summary>
public class FlowEngineWebApplicationFactory : WebApplicationFactory<Program>
{
    static FlowEngineWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("FLOWENGINE_ADMIN_PASSWORD", "TestP@ssw0rd123!");
    }
}
