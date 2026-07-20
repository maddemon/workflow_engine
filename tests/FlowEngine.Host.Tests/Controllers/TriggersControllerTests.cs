using System.Net;
using System.Net.Http.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowEngine.Host.Tests.Controllers;

public class TriggersControllerTests : HostIntegrationTestBase
{
    public TriggersControllerTests(FlowEngineWebApplicationFactory factory)
        : base(factory)
    {
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
}
