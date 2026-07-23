using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// ChatInputNode 单元测试，验证聊天触发负载聚合、缺输入与标量映射行为。
/// 触发器负载由调度器写入 inputs["Input"]（端口名 Input），测试以
/// NodeTestContextFactory.BuildAsync 注入 inputs["input"] 模拟该接线。
/// </summary>
public sealed class ChatInputNodeTests
{
    private static DataBatch TriggerBatch(JsonNode? data) =>
        new() { Items = [new DataItem { Data = data, Success = true, SourceIndex = 0 }] };

    private static Dictionary<string, DataBatch> Inputs(JsonNode? chatPayload) =>
        new(StringComparer.OrdinalIgnoreCase) { ["input"] = TriggerBatch(chatPayload) };

    [Fact]
    public async Task ExecuteAsync_ChatPayload_PreservesFieldsAndAddsTriggeredAt()
    {
        var payload = new JsonObject { ["message"] = "你好", ["sessionId"] = "s1" };
        var node = new ChatInputNode();
        var context = await NodeTestContextFactory.BuildAsync(node, null, Inputs(payload));

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("你好", data["message"]!.GetValue<string>());
        Assert.Equal("s1", data["sessionId"]!.GetValue<string>());
        Assert.NotNull(data["triggeredAt"]);
        Assert.False(data.ContainsKey("welcomeMessage"));
        Assert.False(data.ContainsKey("responseMode"));
    }

    [Fact]
    public async Task ExecuteAsync_ChatPayload_WithWelcomeAndStreaming_ReflectsParams()
    {
        var payload = new JsonObject { ["message"] = "你好", ["sessionId"] = "s1" };
        var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["welcomeMessage"] = "欢迎使用",
            ["responseMode"] = ChatResponseMode.Streaming
        };
        var node = new ChatInputNode();
        var context = await NodeTestContextFactory.BuildAsync(node, parameters, Inputs(payload));

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("你好", data["message"]!.GetValue<string>());
        Assert.Equal("s1", data["sessionId"]!.GetValue<string>());
        Assert.Equal("欢迎使用", data["welcomeMessage"]!.GetValue<string>());
        Assert.Equal("Streaming", data["responseMode"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_NoInput_ReturnsTriggeredAtOnly_NoError()
    {
        var node = new ChatInputNode();
        var context = await NodeTestContextFactory.BuildAsync(node, null, null);

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Null(result.Error);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.NotNull(data["triggeredAt"]);
        Assert.False(data.ContainsKey("message"));
        Assert.False(data.ContainsKey("sessionId"));
        Assert.False(data.ContainsKey("welcomeMessage"));
        Assert.False(data.ContainsKey("responseMode"));
    }

    [Fact]
    public async Task ExecuteAsync_ScalarFirstItem_MapsToMessage()
    {
        var node = new ChatInputNode();
        var context = await NodeTestContextFactory.BuildAsync(node, null, Inputs(JsonValue.Create("hi there")));

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("hi there", data["message"]!.GetValue<string>());
        Assert.NotNull(data["triggeredAt"]);
        Assert.False(data.ContainsKey("sessionId"));
    }

    [Fact]
    public async Task ExecuteAsync_WelcomeMessageOnly_ReflectedWithoutResponseMode()
    {
        var payload = new JsonObject { ["message"] = "hi" };
        var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["welcomeMessage"] = "你好，有什么可以帮你？"
        };
        var node = new ChatInputNode();
        var context = await NodeTestContextFactory.BuildAsync(node, parameters, Inputs(payload));

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("你好，有什么可以帮你？", data["welcomeMessage"]!.GetValue<string>());
        Assert.False(data.ContainsKey("responseMode"));
    }
}
