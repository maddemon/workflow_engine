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

public class QueueDiagnosticTests
{
    private static readonly string _dbName = Guid.NewGuid().ToString();

    [Fact]
    public async Task Queue_And_Execute_Directly()
    {
        var dbContext = CreateDbContext(_dbName);

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

        // Create workflow
        var nodeA = new NodeDefinition
        {
            Id = Guid.NewGuid(),
            Name = "a",
            TypeName = "passThrough",
            IsEntry = true,
            Parameters = [],
            ErrorStrategy = ErrorStrategy.Terminate
        };

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "diagnostic",
            CreatedBy = "test",
            Nodes = [nodeA],
            Connections = []
        };

        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Start execution
        var executionId = await executor.StartAsync(workflow.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Check: execution exists in DB
        var execCheck = await dbContext.ExecutionRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == executionId.Value, TestContext.Current.CancellationToken);
        Assert.NotNull(execCheck); // Should exist

        // Manually dequeue and execute
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var item = await queue.DequeueAsync(cts.Token);
        Assert.NotNull(item);

        // Check: workflow exists in new context
        using var execDbContext = CreateDbContext(_dbName);
        var loadedWorkflow = await execDbContext.Workflows.FirstOrDefaultAsync(w => w.Id == item.WorkflowDefinitionId, TestContext.Current.CancellationToken);
        Assert.NotNull(loadedWorkflow); // Should exist

        // Check: execution record exists in new context
        var executionRecord = await execDbContext.ExecutionRecords
            .FirstOrDefaultAsync(e => e.Id == item.ExecutionRecordId, TestContext.Current.CancellationToken);
        Assert.NotNull(executionRecord); // Should exist

        // Check: execution entry node should be detected
        var passThroughType = nodeRegistry.Get("passThrough");
        Assert.NotNull(passThroughType); // Should exist
        var nodeType = nodeRegistry.Get(nodeA.TypeName);
        Assert.NotNull(nodeType); // Should resolve

        // Check the PassThroughNode ports
        Assert.NotEmpty(nodeType.Ports);
        var inputPorts = nodeType.Ports.Where(p => p.Direction == PortDirection.Input).ToList();
        Assert.NotEmpty(inputPorts); // Should have input ports
        var outputPorts = nodeType.Ports.Where(p => p.Direction == PortDirection.Output).ToList();
        Assert.NotEmpty(outputPorts); // Should have output ports

        // Execute
        await executor.ExecuteLoopAsync(
            loadedWorkflow, item.ExecutionRecordId, item.TriggerPayload, execDbContext,
            TestContext.Current.CancellationToken);

        // Check results
        using var readCtx = CreateDbContext(_dbName);
        var result = await readCtx.ExecutionRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == executionId.Value, TestContext.Current.CancellationToken);
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