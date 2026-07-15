using FlowEngine.Core.Agent;
using FlowEngine.Core.Entities;

namespace FlowEngine.Runtime.Tests.Plugins;

public class AgentMemoryTests
{
    [Fact]
    public void AddMessage_Stores_Message()
    {
        var memory = new AgentMemory(10);

        memory.AddMessage(new LlmMessage { Role = "user", Content = "Hello" });

        Assert.Equal(1, memory.Count);
    }

    [Fact]
    public void AddMessage_Trims_When_Exceeding_Window()
    {
        var memory = new AgentMemory(3);

        memory.AddMessage(new LlmMessage { Role = "user", Content = "Msg1" });
        memory.AddMessage(new LlmMessage { Role = "assistant", Content = "Msg2" });
        memory.AddMessage(new LlmMessage { Role = "user", Content = "Msg3" });
        memory.AddMessage(new LlmMessage { Role = "assistant", Content = "Msg4" });

        Assert.Equal(3, memory.Count);
        var messages = memory.GetMessages();
        Assert.Equal("Msg2", messages[0].Content);
        Assert.Equal("Msg3", messages[1].Content);
        Assert.Equal("Msg4", messages[2].Content);
    }

    [Fact]
    public void AddMessages_Batch_Adds_All()
    {
        var memory = new AgentMemory(10);

        memory.AddMessages([
            new LlmMessage { Role = "user", Content = "A" },
            new LlmMessage { Role = "assistant", Content = "B" }
        ]);

        Assert.Equal(2, memory.Count);
    }

    [Fact]
    public void AddMessages_Trims_When_Exceeding_Window()
    {
        var memory = new AgentMemory(2);

        memory.AddMessages([
            new LlmMessage { Role = "user", Content = "A" },
            new LlmMessage { Role = "assistant", Content = "B" },
            new LlmMessage { Role = "user", Content = "C" }
        ]);

        Assert.Equal(2, memory.Count);
        var messages = memory.GetMessages();
        Assert.Equal("B", messages[0].Content);
        Assert.Equal("C", messages[1].Content);
    }

    [Fact]
    public void GetMessages_Returns_Readonly_Collection()
    {
        var memory = new AgentMemory(10);
        memory.AddMessage(new LlmMessage { Role = "user", Content = "Hello" });

        var messages = memory.GetMessages();
        Assert.Single(messages);
        Assert.IsAssignableFrom<IReadOnlyList<LlmMessage>>(messages);
    }

    [Fact]
    public void Clear_Removes_All_Messages()
    {
        var memory = new AgentMemory(10);
        memory.AddMessage(new LlmMessage { Role = "user", Content = "Hello" });
        memory.AddMessage(new LlmMessage { Role = "assistant", Content = "World" });

        memory.Clear();

        Assert.Equal(0, memory.Count);
    }

    [Fact]
    public void MergeAndReturnAll_Returns_Combined_Messages()
    {
        var memory = new AgentMemory(5);
        memory.AddMessage(new LlmMessage { Role = "system", Content = "System" });
        memory.AddMessage(new LlmMessage { Role = "user", Content = "Hello" });

        var result = memory.MergeAndReturnAll([
            new LlmMessage { Role = "assistant", Content = "Hi" },
            new LlmMessage { Role = "user", Content = "Help" }
        ]);

        Assert.Equal(4, result.Count);
        Assert.Equal("System", result[0].Content);
        Assert.Equal("Hello", result[1].Content);
        Assert.Equal("Hi", result[2].Content);
        Assert.Equal("Help", result[3].Content);
    }

    [Fact]
    public void WindowSize_One_Keeps_Only_Latest()
    {
        var memory = new AgentMemory(1);

        memory.AddMessage(new LlmMessage { Role = "user", Content = "First" });
        memory.AddMessage(new LlmMessage { Role = "assistant", Content = "Second" });
        memory.AddMessage(new LlmMessage { Role = "user", Content = "Third" });

        Assert.Equal(1, memory.Count);
        Assert.Equal("Third", memory.GetMessages()[0].Content);
    }
}
