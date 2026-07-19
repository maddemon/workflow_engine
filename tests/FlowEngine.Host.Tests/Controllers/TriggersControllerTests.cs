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

public class TriggersControllerTests : IClassFixture<FlowEngineWebApplicationFactory>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempRoot;

    public TriggersControllerTests(FlowEngineWebApplicationFactory factory)
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
    public async Task GetAll_AuthenticatedAdmin_ReturnsOkWithTriggers()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "triggers-getall@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var trigger = await SeedTriggerAsync(email, ct);

        var response = await client.GetAsync("/api/v1/triggers", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<TriggerDto>>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Contains(result, t => t.Id == trigger.Id);
    }

    [Fact]
    public async Task GetAll_ByWorkflow_ReturnsOkWithTriggers()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "triggers-getall-wf@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var trigger = await SeedTriggerAsync(email, ct);

        var response = await client.GetAsync($"/api/v1/triggers?workflowDefinitionId={trigger.WorkflowDefinitionId}", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<TriggerDto>>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Contains(result, t => t.Id == trigger.Id);
    }

    [Fact]
    public async Task GetById_Existing_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "triggers-get@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var trigger = await SeedTriggerAsync(email, ct);

        var response = await client.GetAsync($"/api/v1/triggers/{trigger.Id}", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<TriggerDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(trigger.Id, result!.Id);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("triggers-get-notfound@example.com", [RoleConstants.Admin], ct);

        var response = await client.GetAsync($"/api/v1/triggers/{Guid.NewGuid()}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidDto_ReturnsCreated()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "triggers-create@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var workflow = await SeedWorkflowAsync(email, ct);
        var dto = new CreateTriggerDto
        {
            WorkflowDefinitionId = workflow.Id,
            WorkflowVersion = 1,
            Type = TriggerType.Schedule,
            Name = "Daily Trigger",
            Settings = new TriggerSettingsDto
            {
                CronExpression = "0 0 * * *",
            },
        };

        var response = await client.PostAsJsonAsync("/api/v1/triggers", dto, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<TriggerDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(dto.Name, result!.Name);
    }

    [Fact]
    public async Task Create_InvalidDto_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("triggers-create-invalid@example.com", [RoleConstants.Admin], ct);
        var dto = new CreateTriggerDto
        {
            WorkflowDefinitionId = Guid.Empty,
            Name = string.Empty,
        };

        var response = await client.PostAsJsonAsync("/api/v1/triggers", dto, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "triggers-update@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var trigger = await SeedTriggerAsync(email, ct);
        var dto = new UpdateTriggerDto
        {
            Name = "Updated Trigger",
            IsActive = false,
        };

        var response = await client.PutAsJsonAsync($"/api/v1/triggers/{trigger.Id}", dto, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<TriggerDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(dto.Name, result!.Name);
        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("triggers-update-notfound@example.com", [RoleConstants.Admin], ct);
        var dto = new UpdateTriggerDto
        {
            Name = "Updated Trigger",
            IsActive = false,
        };

        var response = await client.PutAsJsonAsync($"/api/v1/triggers/{Guid.NewGuid()}", dto, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_ReturnsNoContent()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "triggers-delete@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var trigger = await SeedTriggerAsync(email, ct);

        var response = await client.DeleteAsync($"/api/v1/triggers/{trigger.Id}", ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("triggers-delete-notfound@example.com", [RoleConstants.Admin], ct);

        var response = await client.DeleteAsync($"/api/v1/triggers/{Guid.NewGuid()}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    private async Task<Trigger> SeedTriggerAsync(string email, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();

        var workflow = await SeedWorkflowAsync(email, ct);
        var trigger = new Trigger
        {
            WorkflowDefinitionId = workflow.Id,
            ProjectId = workflow.ProjectId,
            WorkflowVersion = 1,
            Type = TriggerType.Schedule,
            Name = "Test Trigger",
            IsActive = true,
            Settings = new TriggerSettings(),
        };
        dbContext.Triggers.Add(trigger);
        await dbContext.SaveChangesAsync(ct);
        return trigger;
    }

    private static JsonSerializerOptions TestJsonOptions => HostTestJsonOptions.Default;
}
