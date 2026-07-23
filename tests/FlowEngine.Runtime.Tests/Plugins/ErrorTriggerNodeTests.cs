using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;
using Moq;
using FlowEngine.Core.Abstractions;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// ErrorTriggerNode 单元测试，验证失败触发负载聚合、缺输入、try/catch 行为。
/// 触发器负载由调度器写入 inputs["Input"]（端口名 Input），测试以
/// NodeTestContextFactory.BuildAsync 注入 inputs["input"] 模拟该接线。
/// </summary>
public sealed class ErrorTriggerNodeTests
{
    private static DataBatch TriggerBatch(JsonNode? data) =>
        new() { Items = [new DataItem { Data = data, Success = true, SourceIndex = 0 }] };

    private static Dictionary<string, DataBatch> Inputs(JsonNode? payload) =>
        new(StringComparer.OrdinalIgnoreCase) { ["input"] = TriggerBatch(payload) };

    [Fact]
    public async Task ExecuteAsync_FailurePayload_CopiesFieldsAndAddsTriggeredAt()
    {
        var payload = new JsonObject { ["workflowId"] = "wf-123", ["errorMessage"] = "boom" };
        var node = new ErrorTriggerNode();
        var context = await NodeTestContextFactory.BuildAsync(node, null, Inputs(payload));

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("wf-123", data["workflowId"]!.GetValue<string>());
        Assert.Equal("boom", data["errorMessage"]!.GetValue<string>());
        Assert.NotNull(data["triggeredAt"]);
    }

    [Fact]
    public async Task ExecuteAsync_NoInput_ReturnsTriggeredAtOnly_NoError()
    {
        var node = new ErrorTriggerNode();
        var context = await NodeTestContextFactory.BuildAsync(node, null, null);

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Null(result.Error);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.NotNull(data["triggeredAt"]);
        Assert.False(data.ContainsKey("workflowId"));
        Assert.False(data.ContainsKey("errorMessage"));
    }

    [Fact]
    public async Task ExecuteAsync_FirstItemNotJsonObject_ReturnsTriggeredAtOnly_NoError()
    {
        // 标量输入（非 JsonObject）：payload 保持为空，仅补充 triggeredAt。
        var node = new ErrorTriggerNode();
        var context = await NodeTestContextFactory.BuildAsync(node, null, Inputs(JsonValue.Create("raw")));

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.NotNull(data["triggeredAt"]);
        Assert.False(data.ContainsKey("workflowId"));
    }

    [Fact]
    public async Task ExecuteAsync_InputAlreadyHasTriggeredAt_PreservesIt()
    {
        var payload = new JsonObject { ["workflowId"] = "wf-1", ["triggeredAt"] = "2026-07-12T09:00:00Z" };
        var node = new ErrorTriggerNode();
        var context = await NodeTestContextFactory.BuildAsync(node, null, Inputs(payload));

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("2026-07-12T09:00:00Z", data["triggeredAt"]!.GetValue<string>());
        Assert.Equal("wf-1", data["workflowId"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_LogsOnlyWorkflowId_NotErrorMessage()
    {
        var payload = new JsonObject { ["workflowId"] = "wf-log", ["errorMessage"] = "secret-boom" };
        var node = new ErrorTriggerNode();
        var context = await NodeTestContextFactory.BuildAsync(node, null, Inputs(payload));

        var loggerMock = new Mock<IExecutionLogger>();
        string? capturedMessage = null;
        object?[]? capturedArgs = null;
        loggerMock
            .Setup(l => l.LogInformation(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Callback<string, object?[]>((m, a) => { capturedMessage = m; capturedArgs = a; });
        context.Logger = loggerMock.Object;

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        loggerMock.Verify(l => l.LogInformation(It.IsAny<string>(), It.IsAny<object?[]>()), Times.Once);
        Assert.NotNull(capturedMessage);
        Assert.Contains("errorTrigger", capturedMessage!);
        Assert.NotNull(capturedArgs);
        Assert.DoesNotContain(capturedArgs!, a => a is string s && s.Contains("secret-boom"));
    }
}
