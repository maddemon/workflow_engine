using System.Text.Json.Nodes;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Tests.Scripting;

public sealed class InputContainerTests
{
    [Fact]
    public void Item_Returns_CurrentItem()
    {
        var items = new List<object?> { JsonNode.Parse("""{"value":1}""") };
        var current = JsonNode.Parse("""{"value":1}""");
        var container = new InputContainer(items, current);

        Assert.Same(current, container.item());
    }

    [Fact]
    public void Item_WithNoCurrentItem_ReturnsNull()
    {
        var items = new List<object?> { JsonNode.Parse("""{"value":1}""") };
        var container = new InputContainer(items, null);

        Assert.Null(container.item());
    }

    [Fact]
    public void All_Returns_AllItems()
    {
        var items = new List<object?>
        {
            JsonNode.Parse("""{"value":1}"""),
            JsonNode.Parse("""{"value":2}"""),
        };
        var container = new InputContainer(items, null);

        var result = container.all();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void First_Returns_FirstItem()
    {
        var items = new List<object?>
        {
            JsonNode.Parse("""{"value":1}"""),
            JsonNode.Parse("""{"value":2}"""),
        };
        var container = new InputContainer(items, null);

        var result = container.first() as JsonNode;

        Assert.NotNull(result);
        Assert.Equal(1, result["value"]!.GetValue<int>());
    }

    [Fact]
    public void First_WithEmptyList_ReturnsNull()
    {
        var container = new InputContainer([], null);

        Assert.Null(container.first());
    }

    [Fact]
    public void Last_Returns_LastItem()
    {
        var items = new List<object?>
        {
            JsonNode.Parse("""{"value":1}"""),
            JsonNode.Parse("""{"value":2}"""),
        };
        var container = new InputContainer(items, null);

        var result = container.last() as JsonNode;

        Assert.NotNull(result);
        Assert.Equal(2, result["value"]!.GetValue<int>());
    }

    [Fact]
    public void Last_WithEmptyList_ReturnsNull()
    {
        var container = new InputContainer([], null);

        Assert.Null(container.last());
    }

    [Fact]
    public void Count_Returns_ItemCount()
    {
        var container = new InputContainer(
        [
            JsonNode.Parse("""{"value":1}"""),
            JsonNode.Parse("""{"value":2}"""),
        ], null);

        Assert.Equal(2, container.count());
    }

    [Fact]
    public void Count_WithEmptyList_ReturnsZero()
    {
        var container = new InputContainer([], null);

        Assert.Equal(0, container.count());
    }

    [Fact]
    public void Params_Property_Returns_Params()
    {
        var @params = new Dictionary<string, object> { ["key"] = "value" };
        var container = new InputContainer([], null, @params);

        Assert.NotNull(container.Params);
        Assert.Equal("value", container.Params["key"]);
    }

    [Fact]
    public void Params_WhenNotProvided_ReturnsNull()
    {
        var container = new InputContainer([], null);

        Assert.Null(container.Params);
    }

    [Fact]
    public void Context_Property_Returns_Context()
    {
        var context = new Dictionary<string, object?> { ["executionId"] = "abc" };
        var container = new InputContainer([], null, null, context);

        Assert.NotNull(container.Context);
        Assert.Equal("abc", container.Context["executionId"]);
    }

    [Fact]
    public void Context_WhenNotProvided_ReturnsNull()
    {
        var container = new InputContainer([], null);

        Assert.Null(container.Context);
    }
}
