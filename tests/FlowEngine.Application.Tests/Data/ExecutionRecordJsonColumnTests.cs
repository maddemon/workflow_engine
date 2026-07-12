using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Tests.Data;

public sealed class ExecutionRecordJsonColumnTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;

    public ExecutionRecordJsonColumnTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task NodeRecords_Add_AndMarkModified_IsPersisted()
    {
        var ct = TestContext.Current.CancellationToken;
        var execution = new ExecutionRecord
        {
            WorkflowDefinitionId = Guid.NewGuid(),
            Status = ExecutionStatus.Running,
            StartedAt = DateTime.UtcNow,
            NodeRecords =
            [
                new NodeExecutionRecord
                {
                    NodeDefinitionId = "node-1",
                    RunIndex = 0,
                    StartedAt = DateTime.UtcNow,
                },
            ],
        };
        _dbContext.ExecutionRecords.Add(execution);
        await _dbContext.SaveChangesAsync(ct);

        // 模拟 WorkflowExecutor 追加节点记录的方式：直接 Add 并显式标记属性已修改。
        execution.NodeRecords.Add(new NodeExecutionRecord
        {
            NodeDefinitionId = "node-2",
            RunIndex = 1,
            StartedAt = DateTime.UtcNow,
        });
        _dbContext.Entry(execution).Property(e => e.NodeRecords).IsModified = true;
        await _dbContext.SaveChangesAsync(ct);

        var reloaded = await _dbContext.ExecutionRecords
            .AsNoTracking()
            .FirstAsync(e => e.Id == execution.Id, ct);

        Assert.Equal(2, reloaded.NodeRecords.Count);
    }
}
