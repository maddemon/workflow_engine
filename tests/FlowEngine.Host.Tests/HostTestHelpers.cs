using System.Net.Http.Headers;
using System.Text.Json;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FlowEngine.Host.Tests;

/// <summary>
/// Host 层集成测试共享辅助方法。
/// </summary>
public static class HostTestHelpers
{
    /// <summary>
    /// 默认测试密码。
    /// </summary>
    public const string DefaultPassword = "StrongP@ss1";

    /// <summary>
    /// 创建已认证 HttpClient，并在数据库中创建对应测试用户与角色。
    /// </summary>
    public static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory,
        string email,
        IReadOnlyList<string>? roles = null,
        CancellationToken ct = default)
    {
        await SeedUserAsync(factory, email, DefaultPassword, ct, isActive: true, roles);

        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var user = await GetUserByEmailAsync(factory, email, ct);

        var token = tokenService.GenerateAccessToken(user.Id, user.Email, roles ?? []);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// 创建已认证 HttpClient（不带角色）。
    /// </summary>
    public static Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory,
        string email,
        CancellationToken ct)
        => CreateAuthenticatedClientAsync(factory, email, null, ct);

    /// <summary>
    /// 在数据库中创建测试用户。
    /// </summary>
    public static async Task SeedUserAsync(
        WebApplicationFactory<Program> factory,
        string email,
        string password,
        CancellationToken ct = default,
        bool isActive = true,
        IReadOnlyList<string>? roles = null)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = new User
        {
            Email = email,
            UserName = email.Split('@')[0],
            DisplayName = email,
            PasswordHash = passwordHasher.HashPassword(password),
            IsActive = isActive,
        };
        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync(ct);

        if (roles is not null)
        {
            foreach (var role in roles)
            {
                dbContext.Set<UserRole>().Add(new UserRole { UserId = user.Id, Role = role });
            }

            await dbContext.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// 按邮箱查找测试用户。
    /// </summary>
    public static async Task<User> GetUserByEmailAsync(
        WebApplicationFactory<Program> factory,
        string email,
        CancellationToken ct = default)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var user = await dbContext.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, ct);
        return user ?? throw new InvalidOperationException($"测试用户 {email} 未找到。");
    }

    /// <summary>
    /// 使用独立 SQLite 数据库与审计目录配置 WebApplicationFactory。
    /// </summary>
    public static WebApplicationFactory<Program> CreateIsolatedFactory(
        FlowEngineWebApplicationFactory fixture,
        string tempRoot,
        Action<IWebHostBuilder>? configureBuilder = null)
    {
        var dbDirectory = Path.Combine(tempRoot, "db");
        var auditDirectory = Path.Combine(tempRoot, "audit");
        Directory.CreateDirectory(dbDirectory);
        Directory.CreateDirectory(auditDirectory);

        var dbPath = Path.Combine(dbDirectory, "flowengine.db");
        var factory = fixture.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={dbPath};Mode=ReadWriteCreate");
            builder.UseSetting("Audit:LogPath", auditDirectory);
            configureBuilder?.Invoke(builder);
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IScheduleManager, NoOpScheduleManager>());
                // 禁用所有后台服务（如 Quartz、AuditLogFileSink），避免测试生命周期与后台任务相互影响。
                // 若未来测试需要验证后台服务行为，请针对具体服务调整此处范围。
                services.RemoveAll<IHostedService>();
            });
        });

        factory.ClientOptions.BaseAddress = new Uri("http://localhost");
        return factory;
    }
}

/// <summary>
/// Host 层集成测试基类，提供每个测试独立的 SQLite 数据库与常用辅助方法。
/// </summary>
public abstract class HostIntegrationTestBase : IClassFixture<FlowEngineWebApplicationFactory>, IDisposable
{
    protected readonly WebApplicationFactory<Program> _factory;
    protected readonly string _tempRoot;

    protected HostIntegrationTestBase(
        FlowEngineWebApplicationFactory fixture,
        Action<IWebHostBuilder>? configureBuilder = null)
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "flowengine-tests", Guid.NewGuid().ToString());
        _factory = HostTestHelpers.CreateIsolatedFactory(fixture, _tempRoot, configureBuilder);
    }

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

    protected Task<HttpClient> CreateAuthenticatedClientAsync(
        string email,
        IReadOnlyList<string>? roles = null,
        CancellationToken ct = default)
        => HostTestHelpers.CreateAuthenticatedClientAsync(_factory, email, roles, ct);

    protected Task<HttpClient> CreateAuthenticatedClientAsync(
        string email,
        CancellationToken ct)
        => HostTestHelpers.CreateAuthenticatedClientAsync(_factory, email, ct);

    protected Task SeedUserAsync(
        string email,
        string password,
        CancellationToken ct = default,
        bool isActive = true)
        => HostTestHelpers.SeedUserAsync(_factory, email, password, ct, isActive);

    protected static JsonSerializerOptions TestJsonOptions => HostTestJsonOptions.Default;
}
