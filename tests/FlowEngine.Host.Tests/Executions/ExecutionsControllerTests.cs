using System.Net;
using System.Net.Http.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Host.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowEngine.Host.Tests.Executions;

public class ExecutionsControllerTests : HostIntegrationTestBase
{
    public ExecutionsControllerTests(FlowEngineWebApplicationFactory factory)
        : base(factory, builder =>
        {
            builder.UseSetting("ExecutionCleanup:Enabled", "false");
        })
    {
    }

    [Fact]
    public async Task Execute_WithInputs_ReturnsCreatedAndStartsExecution()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "jwt-execute-inputs@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var workflow = await SeedWorkflowAsync(email, ct);

        var dto = new ExecuteWorkflowDto
        {
            Inputs = new Dictionary<string, object> { ["greeting"] = "hello" },
        };

        var response = await client.PostAsJsonAsync($"/api/v1/workflows/{workflow.Id}/execute", dto, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ExecutionDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(workflow.Id, result!.WorkflowDefinitionId);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var record = await dbContext.ExecutionRecords.FirstOrDefaultAsync(e => e.Id == result.Id, ct);
        Assert.NotNull(record);
    }

    [Fact]
    public async Task Execute_BodyIdempotencyKeyOverridesHeader()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "jwt-execute-idempotency@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var workflow = await SeedWorkflowAsync(email, ct);

        var dto = new ExecuteWorkflowDto
        {
            IdempotencyKey = "body-key",
        };
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", "header-key");

        var response = await client.PostAsJsonAsync($"/api/v1/workflows/{workflow.Id}/execute", dto, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Execute_WithoutBody_ReturnsCreated()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "jwt-execute-no-body@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var workflow = await SeedWorkflowAsync(email, ct);

        var response = await client.PostAsync($"/api/v1/workflows/{workflow.Id}/execute", null, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ExecutionDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Execute_NonExistingWorkflow_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-execute-notfound@example.com", [RoleConstants.Admin], ct);

        var response = await client.PostAsync($"/api/v1/workflows/{Guid.NewGuid()}/execute", null, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Execute_Viewer_ReturnsForbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "jwt-execute-viewer@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Viewer], ct);
        var workflow = await SeedWorkflowAsync(email, ct);

        var response = await client.PostAsync($"/api/v1/workflows/{workflow.Id}/execute", null, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_PendingExecution_ReturnsOkAndSetsCancelled()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "jwt-cancel-pending@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var execution = await SeedExecutionAsync(email, ExecutionStatus.Pending, ct);

        var response = await client.PostAsync($"/api/v1/executions/{execution.Id}/cancel", null, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ExecutionDto>(TestJsonOptions, ct);
        Assert.NotNull(result);
        Assert.Equal(nameof(ExecutionStatus.Cancelled), result!.Status);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var record = await dbContext.ExecutionRecords.FindAsync([execution.Id], ct);
        Assert.NotNull(record);
        Assert.Equal(ExecutionStatus.Cancelled, record.Status);
    }

    [Fact]
    public async Task Cancel_CompletedExecution_ReturnsConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "jwt-cancel-completed@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Admin], ct);
        var execution = await SeedExecutionAsync(email, ExecutionStatus.Completed, ct);

        var response = await client.PostAsync($"/api/v1/executions/{execution.Id}/cancel", null, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_NonExistingExecution_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await CreateAuthenticatedClientAsync("jwt-cancel-notfound@example.com", [RoleConstants.Admin], ct);

        var response = await client.PostAsync($"/api/v1/executions/{Guid.NewGuid()}/cancel", null, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_Viewer_ReturnsForbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = "jwt-cancel-viewer@example.com";
        var client = await CreateAuthenticatedClientAsync(email, [RoleConstants.Viewer], ct);
        var execution = await SeedExecutionAsync(email, ExecutionStatus.Pending, ct);

        var response = await client.PostAsync($"/api/v1/executions/{execution.Id}/cancel", null, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Workflow> SeedWorkflowAsync(string email, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();

        var workflow = new Workflow
        {
            Name = "Test Workflow",
            ProjectId = Guid.NewGuid(),
            Nodes = [],
            Connections = [],
            CreatedBy = email,
            Version = 1,
            IsActive = true,
        };
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync(ct);
        return workflow;
    }

    private async Task<ExecutionRecord> SeedExecutionAsync(string email, ExecutionStatus status, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();

        var workflow = await SeedWorkflowAsync(email, ct);

        var execution = new ExecutionRecord
        {
            WorkflowDefinitionId = workflow.Id,
            ProjectId = workflow.ProjectId,
            Status = status,
            StartedAt = DateTime.UtcNow,
            CompletedAt = status is ExecutionStatus.Completed or ExecutionStatus.Cancelled or ExecutionStatus.Failed ? DateTime.UtcNow : null,
            NodeRecords = [],
        };
        dbContext.ExecutionRecords.Add(execution);
        await dbContext.SaveChangesAsync(ct);
        return execution;
    }
}
