using System.Text.Json.Nodes;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Tests.Scripting;

public sealed class JsonPathTests
{
    [Fact]
    public void GetValue_NullData_ReturnsNull()
    {
        Assert.Null(JsonPath.GetValue(null, "foo"));
    }

    [Fact]
    public void GetValue_NullOrEmptyPath_ReturnsNull()
    {
        var data = new JsonObject { ["foo"] = "bar" };

        Assert.Null(JsonPath.GetValue(data, null));
        Assert.Null(JsonPath.GetValue(data, ""));
    }

    [Fact]
    public void GetValue_SimpleProperty_ReturnsString()
    {
        var data = new JsonObject { ["name"] = "test" };

        Assert.Equal("test", JsonPath.GetValue(data, "name"));
    }

    [Fact]
    public void GetValue_NestedProperty_ReturnsString()
    {
        var data = new JsonObject
        {
            ["user"] = new JsonObject { ["profile"] = new JsonObject { ["age"] = 30 } }
        };

        Assert.Equal("30", JsonPath.GetValue(data, "user.profile.age"));
    }

    [Fact]
    public void GetValue_MissingProperty_ReturnsNull()
    {
        var data = new JsonObject { ["name"] = "test" };

        Assert.Null(JsonPath.GetValue(data, "missing"));
        Assert.Null(JsonPath.GetValue(data, "name.missing"));
    }

    [Fact]
    public void GetValue_ArrayIndex_ReturnsString()
    {
        var data = new JsonObject
        {
            ["items"] = new JsonArray { "a", "b", "c" }
        };

        Assert.Equal("b", JsonPath.GetValue(data, "items[1]"));
    }

    [Fact]
    public void GetValue_ArrayIndex_OutOfBounds_ReturnsNull()
    {
        var data = new JsonObject
        {
            ["items"] = new JsonArray { "a" }
        };

        Assert.Null(JsonPath.GetValue(data, "items[5]"));
        Assert.Null(JsonPath.GetValue(data, "items[-1]"));
    }

    [Fact]
    public void GetValue_ArrayIndex_OnNonArray_ReturnsNull()
    {
        var data = new JsonObject { ["name"] = "test" };

        Assert.Null(JsonPath.GetValue(data, "name[0]"));
    }

    [Fact]
    public void GetValue_MixedPath_ReturnsString()
    {
        var data = new JsonObject
        {
            ["users"] = new JsonArray
            {
                new JsonObject { ["name"] = "alice" },
                new JsonObject { ["name"] = "bob" }
            }
        };

        Assert.Equal("bob", JsonPath.GetValue(data, "users[1].name"));
    }

    [Fact]
    public void GetNode_Object_ReturnsNode()
    {
        var data = new JsonObject { ["user"] = new JsonObject { ["age"] = 30 } };

        var node = JsonPath.GetNode(data, "user");

        Assert.NotNull(node);
        Assert.IsType<JsonObject>(node);
    }

    [Fact]
    public void GetNode_Array_ReturnsNode()
    {
        var data = new JsonObject
        {
            ["items"] = new JsonArray { "a", "b" }
        };

        var node = JsonPath.GetNode(data, "items");

        Assert.NotNull(node);
        Assert.IsType<JsonArray>(node);
    }
}
