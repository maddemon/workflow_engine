using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowEngine.Application.Tests.Data;

/// <summary>
/// D-1：验证全局软删除过滤器 <c>HasQueryFilter(e =&gt; !e.Deleted)</c>。
/// 常规查询默认排除软删除行；需读取已删数据处显式 <c>IgnoreQueryFilters()</c>。
/// </summary>
public sealed class SoftDeleteQueryFilterTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;

    public SoftDeleteQueryFilterTests()
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

    [Fact]
    public async Task Query_ExcludesSoftDeletedRows_ByDefault()
    {
        var wf = new Workflow { Name = "active", CreatedBy = "t", Nodes = [], Connections = [] };
        _dbContext.Workflows.Add(wf);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        wf.Deleted = true;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await _dbContext.Workflows.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await _dbContext.Workflows.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Query_IncludesNonDeletedRows()
    {
        _dbContext.Workflows.Add(new Workflow { Name = "a", CreatedBy = "t", Nodes = [], Connections = [] });
        _dbContext.Workflows.Add(new Workflow { Name = "b", CreatedBy = "t", Nodes = [], Connections = [] });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, await _dbContext.Workflows.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Filter_AppliesToAllEntityDerivedTypes()
    {
        var project = new Project { Name = "p", CreatedBy = Guid.NewGuid().ToString() };
        var cred = new Credential { Name = "c", Type = "t", KeyVersion = "kv" };
        var trigger = new Trigger
        {
            WorkflowDefinitionId = Guid.NewGuid(),
            Name = "t",
            Type = TriggerType.Poll,
            Settings = new TriggerSettings(),
        };
        _dbContext.Projects.Add(project);
        _dbContext.Credentials.Add(cred);
        _dbContext.Triggers.Add(trigger);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        project.Deleted = true;
        cred.Deleted = true;
        trigger.Deleted = true;
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await _dbContext.Projects.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await _dbContext.Credentials.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await _dbContext.Triggers.ToListAsync(TestContext.Current.CancellationToken));

        Assert.Single(await _dbContext.Projects.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await _dbContext.Credentials.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await _dbContext.Triggers.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
    }
}
