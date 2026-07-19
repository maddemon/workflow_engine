using FlowEngine.Core.Configuration;

namespace FlowEngine.Core.Tests;

public class ConfigurationTests
{
    [Fact]
    public void EngineDefaultsOptions_Properties_RoundTrip()
    {
        var options = new EngineDefaultsOptions
        {
            DefaultTimeoutSeconds = 30,
            DefaultMaxRetries = 3,
            DefaultBaseDelaySeconds = 2,
            DefaultMaxDelaySeconds = 120
        };

        Assert.Equal(30, options.DefaultTimeoutSeconds);
        Assert.Equal(3, options.DefaultMaxRetries);
        Assert.Equal(2, options.DefaultBaseDelaySeconds);
        Assert.Equal(120, options.DefaultMaxDelaySeconds);
    }

    [Fact]
    public void EngineDefaultsOptions_Defaults_AreExpected()
    {
        var options = new EngineDefaultsOptions();

        Assert.Null(options.DefaultTimeoutSeconds);
        Assert.Equal(0, options.DefaultMaxRetries);
        Assert.Equal(1, options.DefaultBaseDelaySeconds);
        Assert.Equal(60, options.DefaultMaxDelaySeconds);
    }

    [Fact]
    public void EngineDefaultsOptions_SectionName_IsExpected()
    {
        Assert.Equal("EngineDefaults", EngineDefaultsOptions.SectionName);
    }
}
