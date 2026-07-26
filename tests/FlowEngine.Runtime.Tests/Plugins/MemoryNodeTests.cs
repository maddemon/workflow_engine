using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// MemoryNode 单元测试，验证 Read / Write / Clear 及 JSON 字面量回退。
/// </summary>
public sealed class MemoryNodeTests
{
    private static async Task<NodeExecutionContext> BuildContextAsync(IDictionary<string, JsonNode?>? memory = null)
    {
        return await NodeTestContextFactory.BuildAsync(
            new MemoryNode(),
            inputs: new Dictionary<string, DataBatch>
            {
                [FlowConstants.PortNames.Input] = new DataBatch
                {
                    Items =
                    [
                        new DataItem
                        {
                            Data = JsonNode.Parse("{\"payload\":\"from-input\"}"),
                            Success = true,
                            SourceIndex = 0
                        }
                    ]
                }
            },
            memory: memory).ConfigureAwait(false);
    }

    [Fact]
    public async Task ExecuteAsync_ReadExistingKey_ReturnsValue()
    {
        var memory = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["existing"] = JsonValue.Create("stored")
        };
        var context = await BuildContextAsync(memory);

        var result = await ((INodeType)new MemoryNode { Action = MemoryAction.Read, Key = "existing" }).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("stored", result.Output.Items[0].Data?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_ReadMissingKey_ReturnsError()
    {
        var context = await BuildContextAsync();

        var result = await ((INodeType)new MemoryNode { Action = MemoryAction.Read, Key = "missing" }).ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("KeyNotFound", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_WriteJsonLiteral_StoresParsedValue()
    {
        var context = await BuildContextAsync();

        var result = await ((INodeType)new MemoryNode { Action = MemoryAction.Write, Key = "config", Value = "{\"enabled\":true}" }).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.True(context.Memory["config"]!["enabled"]!.GetValue<bool>());
        Assert.True(result.Output.Items[0].Data!["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ExecuteAsync_WritePlainString_StoresString()
    {
        var context = await BuildContextAsync();

        var result = await ((INodeType)new MemoryNode { Action = MemoryAction.Write, Key = "name", Value = "hello" }).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("hello", context.Memory["name"]!.GetValue<string>());
        Assert.Equal("hello", result.Output.Items[0].Data!.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_WriteWithoutValue_FallsBackToInput()
    {
        var context = await BuildContextAsync();

        var result = await ((INodeType)new MemoryNode { Action = MemoryAction.Write, Key = "input" }).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("from-input", context.Memory["input"]!["payload"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_Clear_RemovesKey()
    {
        var memory = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["toRemove"] = JsonValue.Create("value")
        };
        var context = await BuildContextAsync(memory);

        var result = await ((INodeType)new MemoryNode { Action = MemoryAction.Clear, Key = "toRemove" }).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.False(context.Memory.ContainsKey("toRemove"));
        Assert.True(result.Output.Items[0].Data!.GetValue<bool>());
    }

    [Fact]
    public async Task ExecuteAsync_MissingKey_ReturnsError()
    {
        var context = await BuildContextAsync();

        var result = await ((INodeType)new MemoryNode { Action = MemoryAction.Read, Key = " " }).ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingKey", result.Error?.Code);
    }
}
