using FlowEngine.Core.Entities;
using FlowEngine.Infrastructure.Ai;
using OpenAI;
using OpenAI.Chat;

namespace FlowEngine.Runtime.Tests.Infrastructure;

public sealed class OpenAiLlmClientTests
{
    [Fact]
    public void BuildLlmResponse_StopWithContent_SetsContentAndReason()
    {
        var response = OpenAiLlmClient.BuildLlmResponse("hello", "Stop", null);

        Assert.Equal("hello", response.Content);
        Assert.Equal("Stop", response.FinishReason);
    }

    [Fact]
    public void BuildLlmResponse_LengthFinishReason_PreservesPartialContent()
    {
        // 回归：旧实现仅在 Stop 时填充 Content，导致截断场景下上层拿到空结果。
        var response = OpenAiLlmClient.BuildLlmResponse("partial text", "Length", null);

        Assert.Equal("partial text", response.Content);
        Assert.Equal("Length", response.FinishReason);
    }

    [Fact]
    public void BuildLlmResponse_ContentFilterWithoutContent_LeavesContentNullButMarksReason()
    {
        var response = OpenAiLlmClient.BuildLlmResponse("", "ContentFilter", null);

        Assert.Null(response.Content);
        Assert.Equal("ContentFilter", response.FinishReason);
    }

    [Fact]
    public void BuildLlmResponse_WithToolCalls_MapsToolCalls()
    {
        var toolCall = ChatToolCall.CreateFunctionToolCall("call_1", "get_weather", BinaryData.FromString("{}"));

        var response = OpenAiLlmClient.BuildLlmResponse(null, "ToolCalls", new[] { toolCall });

        Assert.NotNull(response.ToolCalls);
        var mapped = Assert.Single(response.ToolCalls);
        Assert.Equal("call_1", mapped.Id);
        Assert.Equal("get_weather", mapped.Name);
    }

    [Fact]
    public void BuildLlmResponse_EmptyContentAndStop_LeavesContentNull()
    {
        var response = OpenAiLlmClient.BuildLlmResponse("", "Stop", null);

        Assert.Null(response.Content);
        Assert.Equal("Stop", response.FinishReason);
    }
}
