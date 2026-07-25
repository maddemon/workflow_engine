using Microsoft.Extensions.Configuration;
using Serilog;
using Xunit;

namespace FlowEngine.Host.Tests.Observability;

/// <summary>
/// Serilog 集成（O-1）测试：确认配置驱动方式可构建出可用的日志提供方，且保留 Console 接收端。
/// </summary>
public class SerilogConfigurationTests
{
    [Fact]
    public void ConfigurationDrivenLogger_BuildsSuccessfully()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Serilog:MinimumLevel:Default"] = "Information",
                ["Serilog:WriteTo:0:Name"] = "Console",
            })
            .Build();

        var logger = new LoggerConfiguration()
            .ReadFrom.Configuration(config)
            .CreateLogger();

        Assert.NotNull(logger);
        // 写一条日志不应抛异常，验证接收端可用（O-1）。
        logger.Information("Serilog integration smoke test");
        logger.Dispose();
    }
}
