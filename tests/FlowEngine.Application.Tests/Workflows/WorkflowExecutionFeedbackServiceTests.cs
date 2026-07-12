#pragma warning disable xUnit1051 // Use TestContext.Current.CancellationToken

using FlowEngine.Application.Workflows;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Tests.Workflows;

public sealed class WorkflowExecutionFeedbackServiceTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly WorkflowExecutionFeedbackService _service;

    public WorkflowExecutionFeedbackServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase($"FeedbackDb_{Guid.NewGuid()}")
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _service = new WorkflowExecutionFeedbackService(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task GetFeedbackAsync_FailedNode_IncludesExecutionContextAndSuggestedFix()
    {
        var executionId = Guid.NewGuid();
        var record = new ExecutionRecord
        {
            Id = executionId,
            WorkflowDefinitionId = Guid.NewGuid(),
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Failed,
            NodeRecords =
            [
                new NodeExecutionRecord
                {
                    Id = Guid.NewGuid(),
                    NodeDefinitionId = "fetch",
                    RawParameters = new() { ["url"] = "https://bad.example.com" },
                    Output = new NodeExecutionResult
                    {
                        Success = false,
                        Error = new NodeError { Code = "ConnectionRefused", Message = "Connection refused: target host unreachable" },
                    },
                },
            ],
        };
        _dbContext.ExecutionRecords.Add(record);
        await _dbContext.SaveChangesAsync();

        var feedback = await _service.GetFeedbackAsync(executionId);

        Assert.NotNull(feedback);
        Assert.False(feedback!.Success);
        var node = Assert.Single(feedback.Nodes);
        Assert.Equal("fetch", node.NodeId);
        Assert.Equal("Failed", node.Status);
        Assert.Equal("ExecutionError", node.ErrorType);
        Assert.False(string.IsNullOrEmpty(node.SuggestedFix));
        Assert.NotNull(node.ExecutionContext); // 验收标准：执行反馈含 executionContext
    }

    [Fact]
    public async Task GetFeedbackAsync_UnknownExecution_ReturnsNull()
    {
        var feedback = await _service.GetFeedbackAsync(Guid.NewGuid());

        Assert.Null(feedback);
    }
}
