using System.Threading;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Events;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Host.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FlowEngine.Host.Tests.Triggers;

/// <summary>
/// ErrorTriggerEventConsumer 测试：验证失败事件 → 启动匹配工作流的接线，
/// 且不匹配 / 自环 / 未激活工作流不被触发（RED→GREEN：先针对消费者契约写测试，再实现）。
/// </summary>
public class ErrorTriggerEventConsumerTests
{
    private readonly string DbName = Guid.NewGuid().ToString();

    private FlowEngineDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(DbName)
            .Options;
        return new FlowEngineDbContext(options);
    }

    private static Workflow MakeWorkflow(Guid id, bool isActive, params NodeDefinition[] nodes) => new()
    {
        Id = id,
        Name = "wf-" + id,
        CreatedBy = "test",
        IsActive = isActive,
        Nodes = nodes.ToList(),
    };

    private static NodeDefinition ErrorTriggerNode(string? workflowId)
    {
        var parameters = new Dictionary<string, object>();
        if (workflowId is not null)
        {
            parameters["WorkflowId"] = workflowId;
        }

        return new NodeDefinition
        {
            Id = "err",
            TypeName = "errorTrigger",
            Parameters = parameters,
        };
    }

    private ErrorTriggerEventConsumer BuildConsumer(Mock<IEngine> engineMock, out ServiceProvider provider)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEngine>(engineMock.Object);
        services.AddDbContext<FlowEngineDbContext>(o => o.UseInMemoryDatabase(DbName));
        provider = services.BuildServiceProvider();
        return new ErrorTriggerEventConsumer(provider, NullLogger<ErrorTriggerEventConsumer>.Instance);
    }

    private static WorkflowFailedEvent FailedEvent(Guid failedWorkflowId, string message = "boom")
        => new(Guid.NewGuid(), failedWorkflowId, new NodeError { Code = "X", Message = message });

    [Fact]
    public async Task Handle_Matching_ErrorTriggerWorkflow_Starts_Engine()
    {
        var failedId = Guid.NewGuid();
        var errorWfId = Guid.NewGuid();

        using (var seed = CreateDb())
        {
            seed.Workflows.Add(MakeWorkflow(errorWfId, isActive: true, ErrorTriggerNode(failedId.ToString())));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var engineMock = new Mock<IEngine>();
        engineMock
            .Setup(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExecutionId.New());

        var consumer = BuildConsumer(engineMock, out var provider);
        await using (provider)
        {
            await consumer.Handle(FailedEvent(failedId, "boom"), TestContext.Current.CancellationToken);
        }

        engineMock.Verify(e => e.StartAsync(errorWfId, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NonMatching_ErrorTriggerWorkflow_Does_Not_Start_Engine()
    {
        var failedId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var errorWfId = Guid.NewGuid();

        using (var seed = CreateDb())
        {
            // errorTrigger 监控的是 otherId（非 failedId），故本次失败不应触发。
            seed.Workflows.Add(MakeWorkflow(errorWfId, isActive: true, ErrorTriggerNode(otherId.ToString())));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var engineMock = new Mock<IEngine>();
        engineMock
            .Setup(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExecutionId.New());

        var consumer = BuildConsumer(engineMock, out var provider);
        await using (provider)
        {
            await consumer.Handle(FailedEvent(failedId, "boom"), TestContext.Current.CancellationToken);
        }

        engineMock.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SelfLoop_SameWorkflowId_Does_Not_Start_Engine()
    {
        var failedId = Guid.NewGuid();

        using (var seed = CreateDb())
        {
            // errorTrigger 工作流自身与失败工作流同 ID：必须跳过，避免无限自触发。
            seed.Workflows.Add(MakeWorkflow(failedId, isActive: true, ErrorTriggerNode(failedId.ToString())));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var engineMock = new Mock<IEngine>();
        engineMock
            .Setup(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExecutionId.New());

        var consumer = BuildConsumer(engineMock, out var provider);
        await using (provider)
        {
            await consumer.Handle(FailedEvent(failedId, "boom"), TestContext.Current.CancellationToken);
        }

        engineMock.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Wildcard_ErrorTriggerWorkflow_Starts_Engine()
    {
        var failedId = Guid.NewGuid();
        var errorWfId = Guid.NewGuid();

        using (var seed = CreateDb())
        {
            seed.Workflows.Add(MakeWorkflow(errorWfId, isActive: true, ErrorTriggerNode("*")));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var engineMock = new Mock<IEngine>();
        engineMock
            .Setup(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExecutionId.New());

        var consumer = BuildConsumer(engineMock, out var provider);
        await using (provider)
        {
            await consumer.Handle(FailedEvent(failedId, "boom"), TestContext.Current.CancellationToken);
        }

        engineMock.Verify(e => e.StartAsync(errorWfId, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Inactive_ErrorTriggerWorkflow_Does_Not_Start_Engine()
    {
        var failedId = Guid.NewGuid();
        var errorWfId = Guid.NewGuid();

        using (var seed = CreateDb())
        {
            seed.Workflows.Add(MakeWorkflow(errorWfId, isActive: false, ErrorTriggerNode(failedId.ToString())));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var engineMock = new Mock<IEngine>();
        engineMock
            .Setup(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExecutionId.New());

        var consumer = BuildConsumer(engineMock, out var provider);
        await using (provider)
        {
            await consumer.Handle(FailedEvent(failedId, "boom"), TestContext.Current.CancellationToken);
        }

        engineMock.Verify(e => e.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Payload_Contains_FailedWorkflowId_And_ErrorMessage()
    {
        var failedId = Guid.NewGuid();
        var errorWfId = Guid.NewGuid();

        using (var seed = CreateDb())
        {
            seed.Workflows.Add(MakeWorkflow(errorWfId, isActive: true, ErrorTriggerNode(failedId.ToString())));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var engineMock = new Mock<IEngine>();
        object? capturedPayload = null;
        engineMock
            .Setup(e => e.StartAsync(errorWfId, It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, object? p, CancellationToken _) => capturedPayload = p)
            .ReturnsAsync(ExecutionId.New());

        var consumer = BuildConsumer(engineMock, out var provider);
        await using (provider)
        {
            await consumer.Handle(FailedEvent(failedId, "boom"), TestContext.Current.CancellationToken);
        }

        Assert.NotNull(capturedPayload);
        var payloadType = capturedPayload!.GetType();
        var workflowIdProp = payloadType.GetProperty("workflowId")
            ?? throw new InvalidOperationException("payload 缺少 workflowId 属性");
        var errorMessageProp = payloadType.GetProperty("errorMessage")
            ?? throw new InvalidOperationException("payload 缺少 errorMessage 属性");
        Assert.Equal(failedId, (Guid)workflowIdProp.GetValue(capturedPayload)!);
        Assert.Equal("boom", (string)errorMessageProp.GetValue(capturedPayload)!);
    }
}
