using System.Net;
using FlowEngine.Core.Abstractions;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FlowEngine.Host.Tests.Mcp;

/// <summary>
/// MCP /mcp 端点鉴权集成测试。
/// </summary>
public class McpEndpointAuthenticationTests : IClassFixture<FlowEngineWebApplicationFactory>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempRoot;

    /// <summary>
    /// 初始化集成测试工厂，使用临时 SQLite 数据库与独立的审计日志目录。
    /// </summary>
    public McpEndpointAuthenticationTests(FlowEngineWebApplicationFactory factory)
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "flowengine-tests", Guid.NewGuid().ToString());
        var dbDirectory = Path.Combine(_tempRoot, "db");
        var auditDirectory = Path.Combine(_tempRoot, "audit");
        Directory.CreateDirectory(dbDirectory);
        Directory.CreateDirectory(auditDirectory);

        var dbPath = Path.Combine(dbDirectory, "flowengine.db");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={dbPath};Mode=ReadWriteCreate");
            builder.UseSetting("ExecutionCleanup:Enabled", "false");
            builder.UseSetting("Audit:LogPath", auditDirectory);
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IScheduleManager, NoOpScheduleManager>());
                services.RemoveAll<IHostedService>();
            });
        });

        _factory.ClientOptions.BaseAddress = new Uri("http://localhost");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _factory.Dispose();
        try
        {
            Directory.Delete(_tempRoot, true);
        }
        catch
        {
            // 忽略清理临时目录时的错误，不影响测试结果。
        }
    }

    /// <summary>
    /// 未携带凭证访问 /mcp 初始化端点应返回 401 Unauthorized。
    /// </summary>
    [Theory]
    [InlineData("/mcp")]
    [InlineData("/mcp/")]
    public async Task PostMcp_WithoutAuthorization_ReturnsUnauthorized(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Accept", "application/json, text/event-stream");
        client.DefaultRequestHeaders.Add("Mcp-Protocol-Version", "2025-03-26");

        var initBody = """
            {
              "jsonrpc": "2.0",
              "id": 1,
              "method": "initialize",
              "params": {
                "protocolVersion": "2025-03-26",
                "capabilities": {},
                "clientInfo": { "name": "test", "version": "1.0" }
              }
            }
            """;
        var response = await client.PostAsync(path, new StringContent(initBody, System.Text.Encoding.UTF8, "application/json"), ct);
        _ = await response.Content.ReadAsStringAsync(ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// 未携带凭证访问 /mcp SSE 端点应返回 401 Unauthorized。
    /// </summary>
    [Theory]
    [InlineData("/mcp")]
    [InlineData("/mcp/")]
    public async Task GetMcp_WithoutAuthorization_ReturnsUnauthorized(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Accept", "text/event-stream");
        client.DefaultRequestHeaders.Add("Mcp-Protocol-Version", "2025-03-26");

        var response = await client.GetAsync(path, ct);
        _ = await response.Content.ReadAsStringAsync(ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// 未携带凭证访问 /mcp 会话关闭端点应返回 401 Unauthorized。
    /// </summary>
    [Theory]
    [InlineData("/mcp")]
    [InlineData("/mcp/")]
    public async Task DeleteMcp_WithoutAuthorization_ReturnsUnauthorized(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Mcp-Protocol-Version", "2025-03-26");

        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        var response = await client.SendAsync(request, ct);
        _ = await response.Content.ReadAsStringAsync(ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// /mcp Streamable HTTP 端点应已注册在路由表中。
    /// </summary>
    [Fact]
    public void McpEndpoint_IsRegistered()
    {
        var dataSources = _factory.Services.GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>();
        var mcpRoutes = dataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(r => r.RoutePattern.RawText?.TrimEnd('/').Equals("/mcp", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        Assert.NotEmpty(mcpRoutes);

        var methods = mcpRoutes
            .SelectMany(r => r.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Contains("POST", methods, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("GET", methods, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("DELETE", methods, StringComparer.OrdinalIgnoreCase);
    }
}
