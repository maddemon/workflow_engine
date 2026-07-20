using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// WebhookNode 单元测试，验证触发负载输出与默认输出形态。
/// </summary>
public sealed class WebhookNodeTests
{
    private static NodeExecutionContext CreateContext(DataBatch? triggerBatch = null)
    {
        var inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);
        if (triggerBatch is not null)
        {
            inputs["trigger"] = triggerBatch;
        }

        return new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "test" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = "wh1",
                TypeName = "webhook",
                Name = "wh1"
            },
            Inputs = inputs
        };
    }

    [Fact]
    public async Task ExecuteAsync_WithTriggerPayload_ReturnsPayload()
    {
        var payload = new JsonObject { ["event"] = "user.created" };
        var batch = new DataBatch
        {
            Items =
            [
                new DataItem { Data = payload, Success = true, SourceIndex = 0 }
            ]
        };
        var context = CreateContext(batch);

        var result = await new WebhookNode { Method = WebhookMethod.Post, Path = "events" }.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("user.created", result.Output.Items[0].Data!["event"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_NoTrigger_ReturnsDefaultStructure()
    {
        var context = CreateContext();

        var result = await new WebhookNode
        {
            Method = WebhookMethod.Get,
            Path = "my-webhook",
            ResponseMode = WebhookResponseMode.LastNode
        }.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = result.Output.Items[0].Data as JsonObject;
        Assert.NotNull(data);
        Assert.Equal("GET", data["method"]!.GetValue<string>());
        Assert.Equal("my-webhook", data["path"]!.GetValue<string>());
        Assert.NotNull(data["headers"]);
        Assert.NotNull(data["body"]);
    }
}
