using System.Text.Json.Nodes;
using FlowEngine.Core.Tools;

namespace FlowEngine.Core.Tests;

public class NodeDataHelpersTests
{
    [Fact]
    public void TryGetBase64Field_ValidBase64_ReturnsSuccessAndDecodedBytes()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        var base64 = Convert.ToBase64String(payload);
        var data = new JsonObject { ["content"] = base64 };

        var result = NodeDataHelpers.TryGetBase64Field(data, "content", out var bytes);

        Assert.Equal(NodeDataHelpers.Base64FieldResult.Success, result);
        Assert.Equal(payload, bytes);
    }

    [Fact]
    public void TryGetBase64Field_MissingField_ReturnsMissing()
    {
        var data = new JsonObject { ["other"] = "abc" };

        var result = NodeDataHelpers.TryGetBase64Field(data, "content", out var bytes);

        Assert.Equal(NodeDataHelpers.Base64FieldResult.Missing, result);
        Assert.Empty(bytes);
    }

    [Fact]
    public void TryGetBase64Field_NonStringValue_ReturnsMissing()
    {
        var data = new JsonObject { ["content"] = 123 };

        var result = NodeDataHelpers.TryGetBase64Field(data, "content", out var bytes);

        Assert.Equal(NodeDataHelpers.Base64FieldResult.Missing, result);
        Assert.Empty(bytes);
    }

    [Fact]
    public void TryGetBase64Field_NullValue_ReturnsMissing()
    {
        var data = new JsonObject { ["content"] = (JsonNode?)null };

        var result = NodeDataHelpers.TryGetBase64Field(data, "content", out var bytes);

        Assert.Equal(NodeDataHelpers.Base64FieldResult.Missing, result);
        Assert.Empty(bytes);
    }

    [Fact]
    public void TryGetBase64Field_InvalidBase64_ReturnsInvalid()
    {
        var data = new JsonObject { ["content"] = "not!!valid!!base64" };

        var result = NodeDataHelpers.TryGetBase64Field(data, "content", out var bytes);

        Assert.Equal(NodeDataHelpers.Base64FieldResult.Invalid, result);
        Assert.Empty(bytes);
    }

    [Fact]
    public void TryGetBase64Field_NullData_ReturnsMissing()
    {
        var result = NodeDataHelpers.TryGetBase64Field(null, "content", out var bytes);

        Assert.Equal(NodeDataHelpers.Base64FieldResult.Missing, result);
        Assert.Empty(bytes);
    }
}
