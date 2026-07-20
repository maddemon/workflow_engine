#pragma warning disable xUnit1051 // Use TestContext.Current.CancellationToken

using System.Text.Json;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowEngine.Application.Tests.Workflows;

/// <summary>
/// WorkflowRepository 查询测试。
/// </summary>
public sealed class WorkflowRepositoryTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly WorkflowRepository _repository;

    public WorkflowRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _repository = new WorkflowRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task FindReferencingCredentialAsync_NoReference_ReturnsEmpty()
    {
        var credentialId = Guid.NewGuid();
        _dbContext.Workflows.Add(new Workflow
        {
            Name = "No Reference",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1",
                    TypeName = "fetch",
                    Name = "Fetch",
                    Parameters = new Dictionary<string, object> { ["token"] = "other-value" },
                },
            ],
            Connections = [],
        });
        await _dbContext.SaveChangesAsync();

        var result = await _repository.FindReferencingCredentialAsync(credentialId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindReferencingCredentialAsync_StringParameterReference_ReturnsWorkflowName()
    {
        var credentialId = Guid.NewGuid();
        _dbContext.Workflows.Add(new Workflow
        {
            Name = "String Reference",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1",
                    TypeName = "fetch",
                    Name = "Fetch",
                    Parameters = new Dictionary<string, object> { ["credentialId"] = credentialId.ToString() },
                },
            ],
            Connections = [],
        });
        await _dbContext.SaveChangesAsync();

        var result = await _repository.FindReferencingCredentialAsync(credentialId);

        Assert.Single(result);
        Assert.Equal("String Reference", result[0]);
    }

    [Fact]
    public async Task FindReferencingCredentialAsync_JsonElementParameterReference_ReturnsWorkflowName()
    {
        var credentialId = Guid.NewGuid();
        var element = JsonSerializer.Deserialize<JsonElement>("\u0022" + credentialId.ToString() + "\u0022");
        _dbContext.Workflows.Add(new Workflow
        {
            Name = "JsonElement Reference",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1",
                    TypeName = "fetch",
                    Name = "Fetch",
                    Parameters = new Dictionary<string, object> { ["credentialId"] = element },
                },
            ],
            Connections = [],
        });
        await _dbContext.SaveChangesAsync();

        var result = await _repository.FindReferencingCredentialAsync(credentialId);

        Assert.Single(result);
        Assert.Equal("JsonElement Reference", result[0]);
    }
}
