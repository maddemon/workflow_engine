using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
/// Dry-Run 端点集成测试：验证直接传入 DSL 执行，仅 [Authorize] 即可访问。
/// </summary>
public class WorkflowDryRunEndpointTests : IClassFixture<FlowEngineWebApplicationFactory>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempRoot;

    public WorkflowDryRunEndpointTests(FlowEngineWebApplicationFactory factory)
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
        var client = await CreateAuthenticatedClientAsync("jwt-dryrun@example.com", roles: [], ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/dry-run",
            CreateSetNodeRequest(),
            ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ExecutionDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(nameof(ExecutionStatus.DryRunCompleted), result!.Status);
        Assert.Equal(2, result.NodeRecords.Count);
        Assert.All(result.NodeRecords, r => Assert.Equal("Completed", r.Status));
    }

    [Fact]
    public async Task DryRun_WithApiKey_ReturnsOkAndExecutesSetNode()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _) = await CreateClientWithApiKeyAsync("apikey-dryrun@example.com", roles: [], ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/dry-run",
            CreateSetNodeRequest(),
            ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ExecutionDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(nameof(ExecutionStatus.DryRunCompleted), result!.Status);
    }

    [Fact]
    public async Task DryRun_WithoutAuthentication_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/dry-run",
            CreateSetNodeRequest(),
            ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DryRun_MissingNodes_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-dryrun-badrequest@example.com", roles: [], ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/dry-run",
            new DryRunWorkflowRequestDto { Nodes = [], Connections = [] },
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DryRun_MissingConnections_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-dryrun-badrequest2@example.com", roles: [], ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/dry-run",
            new DryRunWorkflowRequestDto { Nodes = [new NodeDefinitionDto { Id = "n1", TypeName = "set", Name = "Set" }], Connections = null! },
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DryRun_EmptyConnections_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-dryrun-empty-connections@example.com", roles: [], ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/dry-run",
            new DryRunWorkflowRequestDto { Nodes = [new NodeDefinitionDto { Id = "n1", TypeName = "set", Name = "Set" }], Connections = [] },
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DryRun_WithNonAdminRole_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-dryrun-viewer@example.com", roles: ["Viewer"], ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/dry-run",
            CreateSetNodeRequest(),
            ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ExecutionDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(nameof(ExecutionStatus.DryRunCompleted), result!.Status);
    }

    [Fact]
    public async Task DryRun_DoesNotCreateWorkflowOrExecutionRecords()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-dryrun-nopersist@example.com", roles: [], ct);

        int workflowCountBefore;
        int executionCountBefore;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
            workflowCountBefore = await dbContext.Workflows.CountAsync(ct);
            executionCountBefore = await dbContext.ExecutionRecords.CountAsync(ct);
        }

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/dry-run",
            CreateSetNodeRequest(),
            ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
            var workflowCountAfter = await dbContext.Workflows.CountAsync(ct);
            var executionCountAfter = await dbContext.ExecutionRecords.CountAsync(ct);
            Assert.Equal(workflowCountBefore, workflowCountAfter);
            Assert.Equal(executionCountBefore, executionCountAfter);
        }
    }

    private static DryRunWorkflowRequestDto CreateSetNodeRequest()
    {
        return new DryRunWorkflowRequestDto
        {
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "set-1",
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
                },
                new NodeDefinitionDto
                {
                    Id = "filter-1",
                    TypeName = "filter",
                    Name = "Filter",
                    Parameters = new Dictionary<string, object>
                    {
                        ["condition"] = "true"
                    },
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "kept", Direction = PortDirection.Output, Type = PortType.Main }
                    ]
                }
            ],
            Connections =
            [
                new ConnectionDto
                {
                    Id = "conn-1",
                    SourceNodeId = "set-1",
                    SourcePortName = "output",
                    TargetNodeId = "filter-1",
                    TargetPortName = "input"
                }
            ]
        };
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
