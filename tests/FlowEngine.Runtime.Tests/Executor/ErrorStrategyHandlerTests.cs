using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Executor;

namespace FlowEngine.Runtime.Tests.Executor;

/// <summary>
/// 错误策略处理器测试。验证 <see cref="ErrorStrategyHandler.Handle"/> 在不同策略下的行为，
/// 以及 <see cref="ErrorStrategyHandler.CreateInputTimeoutResult"/> 的产物结构。
/// </summary>
public class ErrorStrategyHandlerTests
{
    private readonly ErrorStrategyHandler _handler = new();

    [Fact]
    public void Handle_NullResult_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _handler.Handle(null!, "node1", ErrorStrategy.Continue));
    }

    [Fact]
    public void Handle_Terminate_ReturnsOriginalResultUnchanged()
    {
        var result = new NodeExecutionResult { Success = false };

        var returned = _handler.Handle(result, "node1", ErrorStrategy.Terminate);

        Assert.Same(result, returned);
    }

    [Fact]
    public void Handle_Retry_ReturnsOriginalResultUnchanged()
    {
        var result = new NodeExecutionResult { Success = false };

        var returned = _handler.Handle(result, "node1", ErrorStrategy.Retry);

        Assert.Same(result, returned);
    }

    [Fact]
    public void Handle_Continue_WithoutOriginalError_CreatesDefaultErrorAndPreservesData()
    {
        var original = new NodeExecutionResult
        {
            Success = false,
            Output = new DataBatch
            {
                Items = [new DataItem { Success = true, Data = JsonNode.Parse("{\"k\":1}") }]
            }
        };

        var result = _handler.Handle(original, "node1", ErrorStrategy.Continue);

        Assert.False(result.Success);
        Assert.NotNull(result.Output);
        var item = Assert.Single(result.Output!.Items);
        Assert.False(item.Success);
        Assert.NotNull(item.Error);
        Assert.Equal("NodeError", item.Error!.Code);
        Assert.Equal("node1", item.Error.NodeDefinitionId);
        Assert.NotNull(item.Data);
    }

    [Fact]
    public void Handle_Continue_WithExistingError_PreservesErrorAndBranchIndex()
    {
        var original = new NodeExecutionResult
        {
            Success = false,
            BranchIndex = 2,
            Error = new NodeError { Code = "Boom", Message = "boom", NodeDefinitionId = "orig" },
            Output = new DataBatch { Items = [new DataItem { Success = false, Data = JsonNode.Parse("1") }] }
        };

        var result = _handler.Handle(original, "node1", ErrorStrategy.Continue);

        Assert.Equal(2, result.BranchIndex);
        Assert.NotNull(result.Output);
        var item = Assert.Single(result.Output!.Items);
        Assert.NotNull(item.Error);
        Assert.Equal("Boom", item.Error!.Code);
        Assert.Equal("orig", item.Error.NodeDefinitionId);
        Assert.NotNull(item.Data);
    }

    [Fact]
    public void CreateInputTimeoutResult_ProducesStructuredFailure()
    {
        var result = _handler.CreateInputTimeoutResult("node-timeout-1");

        Assert.False(result.Success);
        Assert.NotNull(result.Output);
        var item = Assert.Single(result.Output!.Items);
        Assert.False(item.Success);
        Assert.NotNull(item.Error);
        Assert.Equal("InputTimeout", item.Error!.Code);
        Assert.Equal("Input timed out.", item.Error.Message);
        Assert.Equal("node-timeout-1", item.Error.NodeDefinitionId);
    }
}
