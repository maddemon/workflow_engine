using FlowEngine.Core.Dtos;

namespace FlowEngine.Core.Tests;

public class DtosTests
{
    [Fact]
    public void AgentExecutionInfoDto_Properties_RoundTrip()
    {
        var dto = new AgentExecutionInfoDto
        {
            Model = "gpt-4",
            IterationCount = 3,
            Status = "Completed",
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow.AddMinutes(1),
            ErrorMessage = "error",
            TokenUsage = new TokenUsageDto { PromptTokens = 10, CompletionTokens = 20, TotalTokens = 30 }
        };

        Assert.Equal("gpt-4", dto.Model);
        Assert.Equal(3, dto.IterationCount);
        Assert.Equal("Completed", dto.Status);
        Assert.NotNull(dto.StartedAt);
        Assert.NotNull(dto.CompletedAt);
        Assert.Equal("error", dto.ErrorMessage);
        Assert.NotNull(dto.TokenUsage);
    }

    [Fact]
    public void TokenUsageDto_Properties_RoundTrip()
    {
        var dto = new TokenUsageDto
        {
            PromptTokens = 10,
            CompletionTokens = 20,
            TotalTokens = 30
        };

        Assert.Equal(10, dto.PromptTokens);
        Assert.Equal(20, dto.CompletionTokens);
        Assert.Equal(30, dto.TotalTokens);
    }

    [Fact]
    public void ToolCallRecordDto_Properties_RoundTrip()
    {
        var dto = new ToolCallRecordDto
        {
            Id = "tc1",
            ToolName = "tool",
            Input = new { x = 1 },
            Output = "ok",
            Status = "Completed",
            Duration = 123.4,
            Error = null
        };

        Assert.Equal("tc1", dto.Id);
        Assert.Equal("tool", dto.ToolName);
        Assert.NotNull(dto.Input);
        Assert.Equal("ok", dto.Output);
        Assert.Equal("Completed", dto.Status);
        Assert.Equal(123.4, dto.Duration);
    }

    [Fact]
    public void LlmChunkDto_Properties_RoundTrip()
    {
        var dto = new LlmChunkDto
        {
            Content = "hello",
            Role = "assistant",
            Timestamp = "2026-01-01T00:00:00Z"
        };

        Assert.Equal("hello", dto.Content);
        Assert.Equal("assistant", dto.Role);
        Assert.Equal("2026-01-01T00:00:00Z", dto.Timestamp);
    }

    [Fact]
    public void AgentIterationDto_Properties_RoundTrip()
    {
        var dto = new AgentIterationDto
        {
            Index = 0,
            LlmChunks = [new LlmChunkDto { Content = "hello" }],
            ToolCalls = [new ToolCallRecordDto { Id = "tc" }],
            StartedAt = "2026-01-01T00:00:00Z",
            CompletedAt = "2026-01-01T00:01:00Z"
        };

        Assert.Equal(0, dto.Index);
        Assert.Single(dto.LlmChunks);
        Assert.Single(dto.ToolCalls);
        Assert.Equal("2026-01-01T00:00:00Z", dto.StartedAt);
        Assert.Equal("2026-01-01T00:01:00Z", dto.CompletedAt);
    }

    [Fact]
    public void SubRecordDto_Properties_RoundTrip()
    {
        var dto = new SubRecordDto
        {
            ParentId = "p1",
            AgentName = "agent",
            Records = [new AgentIterationDto { Index = 0 }],
            Status = "Completed"
        };

        Assert.Equal("p1", dto.ParentId);
        Assert.Equal("agent", dto.AgentName);
        Assert.Single(dto.Records);
        Assert.Equal("Completed", dto.Status);
    }

    [Fact]
    public void AgentExecutionResultDto_Properties_RoundTrip()
    {
        var dto = new AgentExecutionResultDto
        {
            AgentInfo = new AgentExecutionInfoDto { Model = "gpt-4" },
            Iterations = [new AgentIterationDto { Index = 0 }],
            SubRecords = [new SubRecordDto { ParentId = "p1" }],
            SystemPrompt = "prompt"
        };

        Assert.Equal("gpt-4", dto.AgentInfo.Model);
        Assert.Single(dto.Iterations);
        Assert.Single(dto.SubRecords);
        Assert.Equal("prompt", dto.SystemPrompt);
    }
}
