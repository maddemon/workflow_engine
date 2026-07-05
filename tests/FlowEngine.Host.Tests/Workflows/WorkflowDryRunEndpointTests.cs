using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FlowEngine.Host.Tests.Workflows;

/// <summary>
/// Dry-Run 端点集成测试：验证仅 [Authorize] 即可访问，JWT 与 API Key 均支持。
/// </summary>
public class WorkflowDryRunEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempRoot;

    public WorkflowDryRunEndpointTests(WebApplicationFactory<Program> factory)
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
    public async Task DryRun_WithJwt_ReturnsOkAndExecutesSetNode()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = await CreateWorkflowWithSetNodeAsync(ct);
        var client = await CreateAuthenticatedClientAsync("jwt-dryrun@example.com", roles: [], ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/dry-run",
            new DryRunWorkflowRequestDto { WorkflowId = workflow.Id, Input = new JsonObject { ["value"] = 1 } },
            ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<DryRunWorkflowResponseDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(workflow.Id, result!.WorkflowId);
        Assert.Single(result.NodeRecords);
        Assert.False(result.NodeRecords[0].Skipped);
        Assert.True(result.NodeRecords[0].Success);
        Assert.Equal("set", result.NodeRecords[0].NodeType);
    }

    [Fact]
    public async Task DryRun_WithApiKey_ReturnsOkAndSkipsHttpNode()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = await CreateWorkflowWithHttpNodeAsync(ct);
        var (client, _, _) = await CreateClientWithApiKeyAsync("apikey-dryrun@example.com", roles: [], ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/dry-run",
            new DryRunWorkflowRequestDto { WorkflowId = workflow.Id, Input = null },
            ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<DryRunWorkflowResponseDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Single(result!.NodeRecords);
        Assert.True(result.NodeRecords[0].Skipped);
        Assert.Contains("httpRequest", result.NodeRecords[0].SkipReason, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task DryRun_WithoutAuthentication_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/dry-run",
            new DryRunWorkflowRequestDto { WorkflowId = Guid.NewGuid(), Input = null },
            ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DryRun_NonExistingWorkflow_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-dryrun-notfound@example.com", roles: [], ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/dry-run",
            new DryRunWorkflowRequestDto { WorkflowId = Guid.NewGuid(), Input = null },
            ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DryRun_WithNonAdminRole_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = await CreateWorkflowWithSetNodeAsync(ct);
        var client = await CreateAuthenticatedClientAsync("jwt-dryrun-viewer@example.com", roles: ["Viewer"], ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/dry-run",
            new DryRunWorkflowRequestDto { WorkflowId = workflow.Id, Input = null },
            ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<DryRunWorkflowResponseDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Single(result!.NodeRecords);
        Assert.False(result.NodeRecords[0].Skipped);
    }

    private async Task<Workflow> CreateWorkflowWithSetNodeAsync(CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();

        var nodeId = Guid.NewGuid();
        var workflow = new Workflow
        {
            Name = "DryRun Set Endpoint",
            CreatedBy = "test",
            IsActive = true,
            Version = 1,
            Nodes =
            [
                new NodeDefinition
                {
                    Id = nodeId,
                    TypeName = "set",
                    Name = "Set",
                    IsEntry = true,
                    Parameters = new Dictionary<string, object>
                    {
                        ["fields"] = JsonSerializer.SerializeToNode(new[] { new { name = "greeting", value = "hello" } }, JsonDefaults.Options)!,
                        ["include"] = "All"
                    },
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }
                    ]
                }
            ],
            Connections = []
        };

        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync(ct);
        return workflow;
    }

    private async Task<Workflow> CreateWorkflowWithHttpNodeAsync(CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();

        var nodeId = Guid.NewGuid();
        var workflow = new Workflow
        {
            Name = "DryRun Http Endpoint",
            CreatedBy = "test",
            IsActive = true,
            Version = 1,
            Nodes =
            [
                new NodeDefinition
                {
                    Id = nodeId,
                    TypeName = "httpRequest",
                    Name = "HTTP",
                    IsEntry = true,
                    Parameters = new Dictionary<string, object>
                    {
                        ["url"] = "https://example.com",
                        ["method"] = "GET"
                    },
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }
                    ]
                }
            ],
            Connections = []
        };

        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync(ct);
        return workflow;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, IReadOnlyList<string>? roles = null, CancellationToken ct = default)
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

        if (roles is not null)
        {
            foreach (var role in roles)
            {
                dbContext.Set<UserRole>().Add(new UserRole { UserId = user.Id, Role = role });
            }

            await dbContext.SaveChangesAsync(ct);
        }

        var token = tokenService.GenerateAccessToken(user.Id, user.Email, roles ?? []);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(HttpClient Client, string Key, Guid UserId)> CreateClientWithApiKeyAsync(
        string email,
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

        var created = await apiKeyService.CreateAsync(user.Id, "Test Key", null, ct);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Key);
        return (client, created.Key, user.Id);
    }

    private static JsonSerializerOptions TestJsonOptions => new(JsonSerializerDefaults.Web);
}
