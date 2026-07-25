using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FlowEngine.Host.Tests;

/// <summary>
/// SEC-2 全局鉴权兜底（FallbackPolicy = RequireAuthenticatedUser）集成测试：
/// 未显式标注 [AllowAnonymous] 的端点默认要求已认证用户；合法匿名端点（/health）可访问。
/// </summary>
public class SecurityHardeningTests : HostIntegrationTestBase
{
    public SecurityHardeningTests(FlowEngineWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Anonymous_GetProtectedEndpoint_Returns401()
    {
        // 未携带任何认证信息的请求访问受保护端点，应被全局 FallbackPolicy 拒绝。
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/workflows", TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Status401Unauthorized, (int)response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_GetHealth_Returns200()
    {
        // /health 已显式 [AllowAnonymous]，匿名应可访问。
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Status200OK, (int)response.StatusCode);
    }
}
