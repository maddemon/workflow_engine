using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests.Executor;

public class MinimalExecuteTests
{
    [Fact]
    public async Task ExecuteLoop_Directly_Works()
    {
        var dbName = Guid.NewGuid().ToString();
        var dbContext = CreateDbContext(dbName);
        var nodeRegistry = new NodeRegistry(
            [new PassThroughNode()],
            NullLogger<NodeRegistry>.Instance);
        var resolver = new ParameterResolver(NullLogger<ParameterResolver>.Instance);
        var contextFactory = new NodeExecutionContextFactory(nodeRegistry, resolver, new TestCredentialAccessor(), new HashSet<string>());
        var errorHandler = new ErrorStrategyHandler();
        var queue = new WorkflowExecutionQueue();

        var executor = new WorkflowExecutor(
            dbContext, nodeRegistry, contextFactory, errorHandler, queue,
            NullLogger<WorkflowExecutor>.Instance);

        var nodeA = new NodeDefinition
        {
            Id = Guid.NewGuid(), Name = "a", TypeName = "passThrough",
            IsEntry = true, Parameters = [], ErrorStrategy = ErrorStrategy.Terminate
        };
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(), Name = "test", CreatedBy = "test",
            Nodes = [nodeA], Connections = []
        };
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Instead of going through StartAsync, manually create execution record
        // and call ExecuteLoopAsync directly with the same db context
        var executionRecord = new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Pending,
            NodeRecords = []
        };
        dbContext.ExecutionRecords.Add(executionRecord);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Now call ExecuteLoopAsync WITH the same dbContext
        await executor.ExecuteLoopAsync(
            workflow,
            executionRecord.Id,
            triggerPayload: null,
            executionStore: dbContext,
            TestContext.Current.CancellationToken);

        // Check results
        var result = await dbContext.ExecutionRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == executionRecord.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(ExecutionStatus.Completed, result.Status);
        Assert.NotEmpty(result.NodeRecords);
    }

    private static FlowEngineDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new FlowEngineDbContext(options);
    }

    private sealed class TestCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult(new CredentialValue());
    }
}