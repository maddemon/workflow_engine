using FlowEngine.Core.Configuration;
using FlowEngine.Infrastructure.Ai;
using Xunit;

namespace FlowEngine.Application.Tests.Ai;

/// <summary>
/// <see cref="SystemLlmClientFactory"/> 测试，覆盖配置合法与缺失场景。
/// </summary>
public class SystemLlmClientFactoryTests
{
    [Fact]
    public void Create_WithValidOptions_ReturnsOpenAiLlmClient()
    {
        var options = new AiOptions
        {
            ApiKey = "sk-test-123",
            Model = "gpt-4o",
            Temperature = 0.3f,
            MaxTokens = 1024,
            BaseEndpoint = null
        };

        var client = SystemLlmClientFactory.Create(options);

        Assert.NotNull(client);
        Assert.IsType<OpenAiLlmClient>(client);
    }

    [Fact]
    public void Create_WithCustomBaseEndpoint_ParsesEndpoint()
    {
        var options = new AiOptions
        {
            ApiKey = "sk-test-123",
            BaseEndpoint = "https://api.example.com/v1"
        };

        var client = SystemLlmClientFactory.Create(options);

        Assert.IsType<OpenAiLlmClient>(client);
    }

    [Fact]
    public void Create_WithNullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SystemLlmClientFactory.Create(null!));
    }

    [Fact]
    public void Create_WithEmptyApiKey_ThrowsInvalidOperationException()
    {
        var options = new AiOptions { ApiKey = "", Model = "gpt-4o" };

        var ex = Assert.Throws<InvalidOperationException>(() => SystemLlmClientFactory.Create(options));
        Assert.Contains("Ai:ApiKey", ex.Message);
    }

    [Fact]
    public void Create_WithWhitespaceApiKey_ThrowsInvalidOperationException()
    {
        var options = new AiOptions { ApiKey = "   " };

        Assert.Throws<InvalidOperationException>(() => SystemLlmClientFactory.Create(options));
    }

    [Fact]
    public void Create_WithInvalidBaseEndpoint_ThrowsInvalidOperationException()
    {
        var options = new AiOptions
        {
            ApiKey = "sk-test-123",
            BaseEndpoint = "not-a-valid-uri"
        };

        var ex = Assert.Throws<InvalidOperationException>(() => SystemLlmClientFactory.Create(options));
        Assert.Contains("基础端点", ex.Message);
    }
}
