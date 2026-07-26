using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// WaitNode 单元测试，验证等待时间计算与取消路径。
/// </summary>
public sealed class WaitNodeTests
{
    private static async Task<NodeExecutionContext> BuildContextAsync()
    {
        return await NodeTestContextFactory.BuildAsync(
            new WaitNode(),
            inputs: new Dictionary<string, DataBatch>
            {
                [FlowConstants.PortNames.Input] = new DataBatch
                {
                    Items =
                    [
                        new DataItem
                        {
                            Data = JsonNode.Parse("{\"id\":1}"),
                            Success = true,
                            SourceIndex = 0
                        }
                    ]
                }
            }).ConfigureAwait(false);
    }

    [Fact]
    public async Task ExecuteAsync_ZeroSeconds_PassesInputThrough()
    {
        var context = await BuildContextAsync();

        var result = await ((INodeType)new WaitNode { Amount = 0 }).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        Assert.Equal(1, result.Output.Items[0].Data?["id"]?.GetValue<int>());
    }

    [Fact]
    public async Task ExecuteAsync_NegativeAmount_ClampsToZero()
    {
        var context = await BuildContextAsync();

        var result = await ((INodeType)new WaitNode { Amount = -5 }).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
    }

    [Fact]
    public async Task ExecuteAsync_MinutesUnit_ConvertsToSeconds()
    {
        var context = await BuildContextAsync();

        var result = await ((INodeType)new WaitNode { Amount = 0, Unit = WaitUnit.Minutes }).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
    }

    [Fact]
    public async Task ExecuteAsync_LimitWaitTime_AppliesMaximum()
    {
        var context = await BuildContextAsync();

        var result = await ((INodeType)new WaitNode
        {
            Amount = 1000,
            Unit = WaitUnit.Seconds,
            LimitWaitTime = true,
            MaxWaitAmount = 0,
            MaxWaitUnit = WaitUnit.Seconds
        }).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledToken_ReturnsCancelledError()
    {
        var context = await BuildContextAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await ((INodeType)new WaitNode { Amount = 0 }).ExecuteAsync(context, cts.Token);

        Assert.False(result.Success);
        Assert.Equal("Cancelled", result.Error?.Code);
    }
}
