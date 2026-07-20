using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowEngine.Host.Tests.Workflows;

/// <summary>
/// Dry-Run 端点集成测试：验证直接传入 DSL 执行，需已认证且拥有 Workflow.Execute 权限（Admin/Editor）；
/// Viewer 等无 Execute 权限的角色返回 403，未认证返回 401。
/// </summary>
public class WorkflowDryRunEndpointTests : HostIntegrationTestBase
{
    public WorkflowDryRunEndpointTests(FlowEngineWebApplicationFactory factory)
        : base(factory, builder =>
        {
            builder.UseSetting("ExecutionCleanup:Enabled", "false");
        })
    {
    }

    [Fact]
    public async Task DryRun_WithJwt_ReturnsOkAndExecutesSetNode()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-dryrun@example.com", roles: ["Editor"], ct);

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
        var (client, _, _) = await CreateClientWithApiKeyAsync("apikey-dryrun@example.com", roles: ["Editor"], ct);

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
    public async Task DryRun_WithEditorRole_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-dryrun-editor@example.com", roles: ["Editor"], ct);

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
    public async Task DryRun_WithViewerRole_ReturnsForbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-dryrun-viewer@example.com", roles: ["Viewer"], ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/workflows/dry-run",
            CreateSetNodeRequest(),
            ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DryRun_DoesNotCreateWorkflowOrExecutionRecords()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-dryrun-nopersist@example.com", roles: ["Editor"], ct);

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
}
