using FlowEngine.Application.ExecutionCleanup;
using Microsoft.Extensions.Configuration;

namespace FlowEngine.Application.Tests.ExecutionCleanup;

/// <summary>
/// 执行清理配置选项测试。
/// </summary>
public class ExecutionCleanupOptionsTests
{
    [Fact]
    public void DefaultOptions_AreValid()
    {
        var options = new ExecutionCleanupOptions();

        Assert.True(options.Enabled);
        Assert.Equal(60, options.IntervalMinutes);
        Assert.Equal(30, options.RetentionDays);
        Assert.Equal(10000, options.MaxRecordsToKeep);
    }

    [Fact]
    public void SectionName_EqualsExpected()
    {
        Assert.Equal("ExecutionCleanup", ExecutionCleanupOptions.SectionName);
    }

    [Fact]
    public void ConfigurationBinding_BindsCorrectly()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExecutionCleanup:Enabled"] = "false",
                ["ExecutionCleanup:IntervalMinutes"] = "30",
                ["ExecutionCleanup:RetentionDays"] = "7",
                ["ExecutionCleanup:MaxRecordsToKeep"] = "5000",
            })
            .Build();

        var options = new ExecutionCleanupOptions();
        configuration.GetSection(ExecutionCleanupOptions.SectionName).Bind(options);

        Assert.False(options.Enabled);
        Assert.Equal(30, options.IntervalMinutes);
        Assert.Equal(7, options.RetentionDays);
        Assert.Equal(5000, options.MaxRecordsToKeep);
    }

    [Fact]
    public void ConfigurationBinding_MissingSection_UsesDefaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var options = new ExecutionCleanupOptions();
        configuration.GetSection(ExecutionCleanupOptions.SectionName).Bind(options);

        Assert.True(options.Enabled);
        Assert.Equal(60, options.IntervalMinutes);
        Assert.Equal(30, options.RetentionDays);
        Assert.Equal(10000, options.MaxRecordsToKeep);
    }

    [Fact]
    public void ConfigurationBinding_PartialValues_KeepsDefaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExecutionCleanup:RetentionDays"] = "14",
            })
            .Build();

        var options = new ExecutionCleanupOptions();
        configuration.GetSection(ExecutionCleanupOptions.SectionName).Bind(options);

        Assert.True(options.Enabled);
        Assert.Equal(60, options.IntervalMinutes);
        Assert.Equal(14, options.RetentionDays);
        Assert.Equal(10000, options.MaxRecordsToKeep);
    }
}
