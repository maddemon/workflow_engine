using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FlowEngine.Host.Tests.Authentication;

/// <summary>
/// API Key 认证集成测试。
/// </summary>
public class ApiKeyAuthenticationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempRoot;

    /// <summary>
    /// 初始化集成测试工厂，使用临时 SQLite 数据库与独立的审计日志目录。
    /// </summary>
    public ApiKeyAuthenticationTests(WebApplicationFactory<Program> factory)
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

    [Fact]
    public async Task GetMe_WithValidApiKey_ReturnsCurrentUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _) = await CreateClientWithApiKeyAsync("validkey@example.com", ct: ct);

        var response = await client.GetAsync("/api/v1/auth/me", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserDto>(TestJsonOptions, ct);
        Assert.NotNull(user);
        Assert.Equal("validkey@example.com", user!.Email);
    }

    [Fact]
    public async Task GetMe_WithInvalidApiKey_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fe_invalid_key");

        var response = await client.GetAsync("/api/v1/auth/me", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_WithRevokedApiKey_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, key, userId) = await CreateClientWithApiKeyAsync("revokedkey@example.com", ct: ct);
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var apiKey = await dbContext.Set<ApiKey>().FirstAsync(x => x.UserId == userId, ct);
        await apiKeyService.RevokeAsync(userId, apiKey.Id, ct);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var response = await client.GetAsync("/api/v1/auth/me", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_WithExpiredApiKey_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, key, _) = await CreateClientWithApiKeyAsync(
            "expiredkey@example.com",
            expiresAt: DateTime.UtcNow.AddSeconds(1),
            ct: ct);

        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var response = await client.GetAsync("/api/v1/auth/me", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateApiKey_AfterCreation_CanListAndRevoke()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("createapikey@example.com", ct);

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/api-keys",
            new CreateApiKeyRequest { Name = "Integration Key" },
            ct);

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateApiKeyResult>(TestJsonOptions, ct);
        Assert.NotNull(created);
        Assert.NotNull(created!.Key);
        Assert.StartsWith("fe_", created.Key);

        var listResponse = await client.GetAsync("/api/v1/auth/api-keys", ct);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<List<ApiKeyDto>>(TestJsonOptions, ct);
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal("Integration Key", list![0].Name);
        Assert.Null(list[0].RevokedAt);

        var revokeResponse = await client.DeleteAsync($"/api/v1/auth/api-keys/{created.Id}", ct);
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        var listAfterRevokeResponse = await client.GetAsync("/api/v1/auth/api-keys", ct);
        var listAfterRevoke = await listAfterRevokeResponse.Content.ReadFromJsonAsync<List<ApiKeyDto>>(TestJsonOptions, ct);
        Assert.NotNull(listAfterRevoke);
        Assert.Single(listAfterRevoke);
        Assert.NotNull(listAfterRevoke![0].RevokedAt);
    }

    [Fact]
    public async Task GetWorkflows_WithApiKeyAndRole_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _) = await CreateClientWithApiKeyAsync(
            "workflowapikey@example.com",
            roles: ["Viewer"],
            ct: ct);

        var response = await client.GetAsync("/api/v1/workflows", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateApiKey_WithApiKeyAuthentication_ReturnsUnauthorizedOrForbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _) = await CreateClientWithApiKeyAsync("apikeycreateskey@example.com", ct: ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/api-keys",
            new CreateApiKeyRequest { Name = "Should Fail" },
            ct);

        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 401 or 403, got {(int)response.StatusCode}.");
    }

    private async Task<(HttpClient Client, string Key, Guid UserId)> CreateClientWithApiKeyAsync(
        string email,
        DateTime? expiresAt = null,
        IReadOnlyList<string>? roles = null,
        CancellationToken ct = default)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<ApiKeyService>();

        var user = new User
        {
            Email = email,
            UserName = email.Split('@')[0],
            DisplayName = email,
            PasswordHash = passwordHasher.HashPassword("StrongP@ss1"),
            IsActive = true,
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

        var created = await apiKeyService.CreateAsync(user.Id, "Test Key", expiresAt, ct);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Key);
        return (client, created.Key, user.Id);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, CancellationToken ct = default)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var user = new User
        {
            Email = email,
            UserName = email.Split('@')[0],
            DisplayName = email,
            PasswordHash = passwordHasher.HashPassword("StrongP@ss1"),
            IsActive = true,
        };
        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync(ct);

        var token = tokenService.GenerateAccessToken(user.Id, user.Email, []);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static JsonSerializerOptions TestJsonOptions => new(JsonSerializerDefaults.Web);
}
