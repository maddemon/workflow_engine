using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.Scripting;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Registry;
using FlowEngine.Runtime.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowEngine.Runtime.Tests.Executor;

/// <summary>
/// 验证失败执行时的真实节点错误被正确带入 <see cref="WorkflowFailedEvent.Error"/>：
/// 内核从失败节点记录提取真实错误并经由 <see cref="IExecutionSideEffects.PublishCompletedAsync"/> 传出。
/// </summary>
public class WorkflowFailedEventEnrichmentTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private sealed class TestCredentialAccessor : ICredentialAccessor
    {
        public CredentialValue? Resolve(string reference) => null;

        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult(new CredentialValue());
    }

    private sealed class CapturingEventBus : IEventBus
    {
        public List<object> Published { get; } = new();

        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            Published.Add(eventInstance!);
            return Task.CompletedTask;
        }

        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : IDomainEvent
            => throw new NotSupportedException();
    }

    private static FlowEngineDbContext CreateDbContext(string name)
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new FlowEngineDbContext(options);
    }

    private static NodeDefinition CreateNode(string id, string typeName, bool isEntry)
        => new() { Id = id, TypeName = typeName, IsEntry = isEntry };

    [Fact]
    public async Task Failed_Workflow_Publishes_Event_With_Real_NodeError()
    {
        var dbContext = CreateDbContext(_dbName);
        var nodeRegistry = new NodeRegistry(
            new INodeType[] { new FailingNode() },
            NullLogger<NodeRegistry>.Instance);

        var resolver = new ParameterResolver(
            NullLogger<ParameterResolver>.Instance,
            Options.Create(new JsEngineOptions()),
            new ScriptCache(Options.Create(new JsEngineOptions())));
        var contextFactory = new NodeExecutionContextFactory(
            nodeRegistry,
            new ScriptCache(Options.Create(new JsEngineOptions())),
            resolver,
            new TestCredentialAccessor(),
            new HashSet<string>());
        var errorHandler = new ErrorStrategyHandler();
        var executionQueue = new WorkflowExecutionQueue();
        var eventBus = new CapturingEventBus();

        var executor = new WorkflowExecutor(
            dbContext,
            nodeRegistry,
            contextFactory,
            errorHandler,
            executionQueue,
            NullLogger<WorkflowExecutor>.Instance,
            NullLogger<WorkflowSchedulerKernel>.Instance,
            new SecretMasker(),
            eventBus);

        var node = CreateNode("fail", "failing", isEntry: true);
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "fail-wf",
            CreatedBy = "test",
            Nodes = [node],
            Connections = [],
        };
        dbContext.Workflows.Add(workflow);
        await dbContext.SaveChangesAsync();

        var executionRecord = new ExecutionRecord
        {
            WorkflowDefinitionId = workflow.Id,
            ProjectId = null,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Pending,
            NodeRecords = [],
        };
        dbContext.ExecutionRecords.Add(executionRecord);
        await dbContext.SaveChangesAsync();

        await executor.ExecuteLoopAsync(workflow, executionRecord.Id, null, dbContext, TestContext.Current.CancellationToken);

        var failedEvent = Assert.Single(eventBus.Published.OfType<WorkflowFailedEvent>());
        Assert.Equal(workflow.Id, failedEvent.WorkflowDefinitionId);
        Assert.NotNull(failedEvent.Error);
        Assert.Equal("TestFailure", failedEvent.Error!.Code);
        Assert.Equal("测试失败。", failedEvent.Error.Message);
    }
}
