using System.Globalization;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// ManualTriggerNode 单元测试，验证输出包含 triggeredAt。
/// </summary>
public sealed class ManualTriggerNodeTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsTriggeredAt()
    {
        var context = await NodeTestContextFactory.BuildAsync(new ManualTriggerNode());
        var before = DateTime.UtcNow.AddSeconds(-1);

        var result = await new ManualTriggerNode().ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var triggeredAt = result.Output.Items[0].Data?["triggeredAt"]?.GetValue<string>();
        Assert.NotNull(triggeredAt);
        var parsed = DateTime.Parse(triggeredAt, null, DateTimeStyles.RoundtripKind);
        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
        Assert.True(parsed >= before && parsed <= DateTime.UtcNow.AddSeconds(1));
    }
}
