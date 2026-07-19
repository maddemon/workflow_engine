using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FlowEngine.Host.Tests.Controllers;

public class WorkflowsControllerTests : IClassFixture<FlowEngineWebApplicationFactory>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempRoot;

    public WorkflowsControllerTests(FlowEngineWebApplicationFactory factory)
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
    public async Task GetAll_AuthenticatedAdmin_ReturnsOkWithWorkflows()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "workflows-getall@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var workflow = await SeedWorkflowAsync(email, ct);

        var response = await client.GetAsync("/api/v1/workflows", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<WorkflowSummaryDto>>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Contains(result.Items, w => w.Id == workflow.Id);
    }

    [Fact]
    public async Task GetAll_ByProject_ReturnsOkWithWorkflows()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "workflows-getall-project@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var workflow = await SeedWorkflowAsync(email, ct);

        var response = await client.GetAsync($"/api/v1/workflows?projectId={workflow.ProjectId}", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<WorkflowSummaryDto>>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Contains(result.Items, w => w.Id == workflow.Id);
    }

    [Fact]
    public async Task Get_Existing_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "workflows-get@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var workflow = await SeedWorkflowAsync(email, ct);

        var response = await client.GetAsync($"/api/v1/workflows/{workflow.Id}", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<WorkflowDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(workflow.Id, result!.Id);
    }

    [Fact]
    public async Task Get_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("workflows-get-notfound@example.com", [RoleConstants.Admin], ct);

        var response = await client.GetAsync($"/api/v1/workflows/{Guid.NewGuid()}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsCreated()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("workflows-create@example.com", [RoleConstants.Admin], ct);
        var dto = new CreateWorkflowDto
        {
            Name = "New Workflow",
            Nodes = [],
            Connections = [],
        };

        var response = await client.PostAsJsonAsync("/api/v1/workflows", dto, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<WorkflowDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(dto.Name, result!.Name);
    }

    [Fact]
    public async Task Create_InvalidDto_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("workflows-create-invalid@example.com", [RoleConstants.Admin], ct);
        var dto = new CreateWorkflowDto { Name = string.Empty };

        var response = await client.PostAsJsonAsync("/api/v1/workflows", dto, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "workflows-update@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var workflow = await SeedWorkflowAsync(email, ct);
        var dto = new UpdateWorkflowDto
        {
            Name = "Updated Name",
            IsActive = true,
            Nodes = [],
            Connections = [],
        };

        var response = await client.PutAsJsonAsync($"/api/v1/workflows/{workflow.Id}", dto, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<WorkflowDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(dto.Name, result!.Name);
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("workflows-update-notfound@example.com", [RoleConstants.Admin], ct);
        var dto = new UpdateWorkflowDto
        {
            Name = "Updated Name",
            IsActive = true,
            Nodes = [],
            Connections = [],
        };

        var response = await client.PutAsJsonAsync($"/api/v1/workflows/{Guid.NewGuid()}", dto, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_ReturnsNoContent()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "workflows-delete@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var workflow = await SeedWorkflowAsync(email, ct);

        var response = await client.DeleteAsync($"/api/v1/workflows/{workflow.Id}", ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("workflows-delete-notfound@example.com", [RoleConstants.Admin], ct);

        var response = await client.DeleteAsync($"/api/v1/workflows/{Guid.NewGuid()}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetVersions_Existing_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "workflows-versions@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var workflow = await SeedWorkflowAsync(email, ct);

        var response = await client.GetAsync($"/api/v1/workflows/{workflow.Id}/versions", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<int>>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Contains(workflow.Version, result);
    }

    [Fact]
    public async Task GetVersion_Existing_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "workflows-version@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var workflow = await SeedWorkflowAsync(email, ct);

        var response = await client.GetAsync($"/api/v1/workflows/{workflow.Id}/versions/{workflow.Version}", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<WorkflowDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(workflow.Id, result!.Id);
        Assert.Equal(workflow.Version, result.Version);
    }

    [Fact]
    public async Task Export_Existing_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "workflows-export@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var workflow = await SeedWorkflowAsync(email, ct);

        var response = await client.GetAsync($"/api/v1/workflows/{workflow.Id}/export", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<WorkflowExportResult>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(workflow.Name, result!.Name);
    }

    [Fact]
    public async Task ExportBatch_ValidIds_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "workflows-export-batch@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var workflow = await SeedWorkflowAsync(email, ct);
        var request = new ExportBatchRequest { Ids = [workflow.Id] };

        var response = await client.PostAsJsonAsync("/api/v1/workflows/export-batch", request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<List<WorkflowExportResult>>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(workflow.Name, result![0].Name);
    }

    [Fact]
    public async Task ExportBatch_EmptyIds_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("workflows-export-batch-empty@example.com", [RoleConstants.Admin], ct);
        var request = new ExportBatchRequest { Ids = [] };

        var response = await client.PostAsJsonAsync("/api/v1/workflows/export-batch", request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Import_ValidJson_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("workflows-import@example.com", [RoleConstants.Admin], ct);
        var json = """
                   {
                     "name": "Imported Workflow",
                     "version": 1,
                     "nodes": [],
                     "connections": []
                   }
                   """;
        var request = new ImportWorkflowRequest { Json = json, ImportedBy = "test" };

        var response = await client.PostAsJsonAsync("/api/v1/workflows/import", request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ImportResult>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.True(result!.Success);
    }

    [Fact]
    public async Task Import_InvalidJson_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("workflows-import-invalid@example.com", [RoleConstants.Admin], ct);
        var request = new ImportWorkflowRequest { Json = "not-json", ImportedBy = "test" };

        var response = await client.PostAsJsonAsync("/api/v1/workflows/import", request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ImportBatch_ValidJson_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("workflows-import-batch@example.com", [RoleConstants.Admin], ct);
        var json = """
                   [
                     {
                       "name": "Imported Workflow 1",
                       "version": 1,
                       "nodes": [],
                       "connections": []
                     }
                   ]
                   """;
        var request = new ImportBatchRequest { Json = json, ImportedBy = "test" };

        var response = await client.PostAsJsonAsync("/api/v1/workflows/import-batch", request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<BatchImportResult>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(1, result!.SuccessCount);
    }

    [Fact]
    public async Task DryRun_ValidRequest_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("workflows-dryrun@example.com", [RoleConstants.Admin], ct);
        var request = new DryRunWorkflowRequestDto
        {
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "trigger-1",
                    TypeName = "manualTrigger",
                    Name = "Manual Trigger",
                },
                new NodeDefinitionDto
                {
                    Id = "set-1",
                    TypeName = "set",
                    Name = "Set",
                },
            ],
            Connections =
            [
                new ConnectionDto
                {
                    Id = "conn-1",
                    SourceNodeId = "trigger-1",
                    SourcePortName = "output",
                    TargetNodeId = "set-1",
                    TargetPortName = "input",
                },
            ],
        };

        var response = await client.PostAsJsonAsync("/api/v1/workflows/dry-run", request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ExecutionDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DryRun_MissingNodes_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("workflows-dryrun-nodes@example.com", [RoleConstants.Admin], ct);
        var request = new DryRunWorkflowRequestDto
        {
            Nodes = [],
            Connections = [new ConnectionDto { Id = "c1", SourceNodeId = "a", TargetNodeId = "b" }],
        };

        var response = await client.PostAsJsonAsync("/api/v1/workflows/dry-run", request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DryRun_MissingConnections_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("workflows-dryrun-connections@example.com", [RoleConstants.Admin], ct);
        var request = new DryRunWorkflowRequestDto
        {
            Nodes = [new NodeDefinitionDto { Id = "a", TypeName = "manualTrigger", Name = "Trigger" }],
            Connections = [],
        };

        var response = await client.PostAsJsonAsync("/api/v1/workflows/dry-run", request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

    private async Task<Workflow> SeedWorkflowAsync(string email, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();

        var user = await dbContext.Set<User>().FirstAsync(u => u.Email == email, ct);
        var workflow = new Workflow
        {
            Name = "Test Workflow",
            Nodes = [],
            Connections = [],
            CreatedBy = user.Id.ToString(),
            Version = 1,
            IsActive = true,
        };
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync(ct);
        return workflow;
    }

    private static JsonSerializerOptions TestJsonOptions => HostTestJsonOptions.Default;
}
