using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// ChatManualNode 单元测试，验证手动聊天触发负载的归一化与触发时间补充。
/// </summary>
public sealed class ChatManualNodeTests
{
    private static DataBatch MakeInputBatch(JsonNode? data) =>
        new()
        {
            Items =
            [
                new DataItem { Data = data, Success = true, SourceIndex = 0 }
            ]
        };

    [Fact]
    public async Task ExecuteAsync_WithChatPayload_ReturnsSameFieldsAndTriggeredAt()
    {
        var payload = new JsonObject
        {
            ["message"] = "帮我查下天气",
            ["sessionId"] = "s1"
        };
        var inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
        {
            ["input"] = MakeInputBatch(payload)
        };

        var context = await NodeTestContextFactory.BuildAsync(new ChatManualNode(), null, inputs);

        var result = await new ChatManualNode().ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = result.Output.Items[0].Data as JsonObject;
        Assert.NotNull(data);
        Assert.Equal("帮我查下天气", data["message"]!.GetValue<string>());
        Assert.Equal("s1", data["sessionId"]!.GetValue<string>());
        Assert.NotNull(data["triggeredAt"]);
        Assert.NotEmpty(data["triggeredAt"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_NoInput_ReturnsTriggeredAtOnlyWithoutError()
    {
        var context = await NodeTestContextFactory.BuildAsync(new ChatManualNode(), null, null);

        var result = await new ChatManualNode().ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = result.Output.Items[0].Data as JsonObject;
        Assert.NotNull(data);
        Assert.True(data.ContainsKey("triggeredAt"));
        Assert.False(data.ContainsKey("message"));
    }

    [Fact]
    public async Task ExecuteAsync_ScalarFirstItemData_MapsToMessage()
    {
        var inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
        {
            ["input"] = MakeInputBatch(JsonValue.Create("纯文本消息"))
        };

        var context = await NodeTestContextFactory.BuildAsync(new ChatManualNode(), null, inputs);

        var result = await new ChatManualNode().ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = result.Output.Items[0].Data as JsonObject;
        Assert.NotNull(data);
        Assert.Equal("纯文本消息", data["message"]!.GetValue<string>());
        Assert.NotNull(data["triggeredAt"]);
    }
}
