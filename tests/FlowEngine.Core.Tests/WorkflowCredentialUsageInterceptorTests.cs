using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowEngine.Core.Tests;

/// <summary>
/// 验证 <see cref="WorkflowCredentialUsageInterceptor"/> 在 SaveChanges 事务内原子维护
/// <see cref="WorkflowCredentialUsage"/> 关联表：新增/修改/删除工作流时正确增删凭据引用行。
/// </summary>
public sealed class WorkflowCredentialUsageInterceptorTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;

    public WorkflowCredentialUsageInterceptorTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static Workflow BuildWorkflow(Guid credentialId)
        => new()
        {
            Name = "wf",
            CreatedBy = "tester",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1",
                    TypeName = "Http",
                    Name = "step1",
                    Parameters = new Dictionary<string, object> { ["credential"] = credentialId.ToString() },
                },
            ],
            Connections = [],
        };

    [Fact]
    public async Task SaveChanges_NewWorkflow_AddsCredentialUsageRows()
    {
        var credId = Guid.NewGuid();
        _dbContext.Workflows.Add(BuildWorkflow(credId));

        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var usages = await _dbContext.WorkflowCredentialUsages
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Single(usages);
        Assert.Equal(credId, usages[0].CredentialId);
        Assert.Equal("n1", usages[0].NodeId);
    }

    [Fact]
    public async Task SaveChanges_ModifiedWorkflow_ReplacesCredentialUsageRows_NoLeak()
    {
        var oldCred = Guid.NewGuid();
        var wf = BuildWorkflow(oldCred);
        _dbContext.Workflows.Add(wf);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var newCred = Guid.NewGuid();
        wf.Nodes =
        [
            new NodeDefinition
            {
                Id = "n1",
                TypeName = "Http",
                Name = "step1",
                Parameters = new Dictionary<string, object> { ["credential"] = newCred.ToString() },
            },
        ];

        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var usages = await _dbContext.WorkflowCredentialUsages
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Single(usages);
        Assert.Equal(newCred, usages[0].CredentialId);
        Assert.DoesNotContain(usages, u => u.CredentialId == oldCred);
    }

    [Fact]
    public async Task SaveChanges_DeletedWorkflow_RemovesCredentialUsageRows()
    {
        var credId = Guid.NewGuid();
        var wf = BuildWorkflow(credId);
        _dbContext.Workflows.Add(wf);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Single(await _dbContext.WorkflowCredentialUsages
            .ToListAsync(TestContext.Current.CancellationToken));

        _dbContext.Workflows.Remove(wf);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await _dbContext.WorkflowCredentialUsages
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveChanges_UnrelatedWorkflow_Untouched()
    {
        var credA = Guid.NewGuid();
        var credB = Guid.NewGuid();
        var wfA = BuildWorkflow(credA);
        _dbContext.Workflows.Add(wfA);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // 新增 wfB，验证 wfA 的引用行不被波及（增量维护）。
        var wfB = BuildWorkflow(credB);
        _dbContext.Workflows.Add(wfB);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var usages = await _dbContext.WorkflowCredentialUsages
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, usages.Count);
        Assert.Contains(usages, u => u.CredentialId == credA);
        Assert.Contains(usages, u => u.CredentialId == credB);
    }
}
