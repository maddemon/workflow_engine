using FlowEngine.Core.Enums;
using FlowEngine.Core.Triggers;

namespace FlowEngine.Core.Tests.Triggers;

public sealed class TriggerTypeRegistryTests
{
    private readonly TriggerTypeRegistry _registry = new();

    [Fact]
    public void Constructor_SeedsBuiltInTypesFromEnum()
    {
        var all = _registry.GetAll().Select(t => t.Type).OrderBy(t => t).ToList();

        var expected = Enum.GetNames<TriggerType>().OrderBy(t => t).ToList();
        Assert.Equal(expected, all);
    }

    [Fact]
    public void IsKnown_BuiltInType_ReturnsTrue()
    {
        Assert.True(_registry.IsKnown("Schedule"));
        Assert.True(_registry.IsKnown("schedule")); // 大小写不敏感
        Assert.True(_registry.IsKnown("Webhook"));
        Assert.True(_registry.IsKnown("Poll"));
    }

    [Fact]
    public void IsKnown_UnknownType_ReturnsFalse()
    {
        Assert.False(_registry.IsKnown("Custom"));
        Assert.False(_registry.IsKnown(string.Empty));
    }

    [Fact]
    public void Register_CustomType_BecomesKnown()
    {
        _registry.Register("WebhookV2", "Webhook V2");

        Assert.True(_registry.IsKnown("WebhookV2"));
        var metadata = _registry.GetAll().Single(t => t.Type == "WebhookV2");
        Assert.Equal("Webhook V2", metadata.DisplayName);
    }

    [Fact]
    public void Register_NullOrEmptyType_Throws()
    {
        Assert.Throws<ArgumentException>(() => _registry.Register(string.Empty, "x"));
        Assert.Throws<ArgumentException>(() => _registry.Register("   ", "x"));
    }
}
