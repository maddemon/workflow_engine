using System.Text.Json.Nodes;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Tests.Scripting;

public sealed class NodeOutputTests
{
    [Fact]
    public void Json_Returns_DataItems()
    {
        var items = new List<object?>
        {
            JsonNode.Parse("""{"value":1}"""),
            JsonNode.Parse("""{"value":2}"""),
        };
        var output = new NodeOutput(items);

        Assert.Equal(2, output.Json.Count);
    }

    [Fact]
    public void Params_WhenProvided_ReturnsParams()
    {
        var @params = new Dictionary<string, object> { ["key"] = "val" };
        var output = new NodeOutput([], @params);

        Assert.NotNull(output.Params);
        Assert.Equal("val", output.Params["key"]);
    }

    [Fact]
    public void Context_WhenProvided_ReturnsContext()
    {
        var context = new Dictionary<string, object?> { ["executionId"] = "abc" };
        var output = new NodeOutput([], null, context);

        Assert.NotNull(output.Context);
        Assert.Equal("abc", output.Context["executionId"]);
    }

    [Fact]
    public void RunIndex_Default_Zero()
    {
        var output = new NodeOutput([]);

        Assert.Equal(0, output.RunIndex);
    }

    [Fact]
    public void RunIndex_WhenSpecified_ReturnsValue()
    {
        var output = new NodeOutput([], null, null, 3);

        Assert.Equal(3, output.RunIndex);
    }

    [Fact]
    public void Constructor_WithNullJson_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new NodeOutput(null!));
    }

    [Fact]
    public void Json_WithEmptyList_ReturnsEmpty()
    {
        var output = new NodeOutput([]);

        Assert.Empty(output.Json);
    }
}
