using System.Text.Json.Nodes;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// ScheduleTriggerNode 单元测试，验证输出包含时间戳与调度参数。
/// </summary>
public sealed class ScheduleTriggerNodeTests
{
    [Fact]
    public async Task ExecuteAsync_DefaultInterval_ReturnsTimestampAndInterval()
    {
        var context = await NodeTestContextFactory.BuildAsync(new ScheduleTriggerNode());

        var result = await new ScheduleTriggerNode { Interval = ScheduleInterval.Hours, IntervalValue = 2 }.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = result.Output.Items[0].Data as JsonObject;
        Assert.NotNull(data);
        Assert.NotNull(data["timestamp"]?.GetValue<string>());
        Assert.Equal("Hours", data["interval"]!.GetValue<string>());
        Assert.Equal(2, data["intervalValue"]!.GetValue<int>());
    }

    [Fact]
    public async Task ExecuteAsync_WithCronExpression_ReturnsCronExpression()
    {
        var context = await NodeTestContextFactory.BuildAsync(new ScheduleTriggerNode());

        var result = await new ScheduleTriggerNode
        {
            Interval = ScheduleInterval.Days,
            IntervalValue = 1,
            CronExpression = "0 9 * * 1-5"
        }.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("0 9 * * 1-5", result.Output.Items[0].Data!["cronExpression"]!.GetValue<string>());
    }
}
