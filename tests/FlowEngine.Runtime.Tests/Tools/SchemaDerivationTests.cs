using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Tools;

namespace FlowEngine.Runtime.Tests.Tools;

public class SchemaDerivationTests
{
    [Fact]
    public void DeriveSchema_NullParameters_ReturnsNull()
    {
        var result = SchemaDerivation.DeriveSchema(null);
        Assert.Null(result);
    }

    [Fact]
    public void DeriveSchema_EmptyParameters_ReturnsNull()
    {
        var result = SchemaDerivation.DeriveSchema([]);
        Assert.Null(result);
    }

    [Fact]
    public void DeriveSchema_SingleStringParam_ReturnsCorrectSchema()
    {
        var parameters = new List<ParameterDefinition>
        {
            new() { Name = "query", DisplayName = "Query", Type = ParameterType.String, Required = true, Description = "Search query" }
        };

        var schema = SchemaDerivation.DeriveSchema(parameters);

        Assert.NotNull(schema);
        Assert.Equal("object", schema!["type"]?.GetValue<string>());
        var props = schema["properties"] as JsonObject;
        Assert.NotNull(props);
        Assert.Equal("string", props!["query"]?["type"]?.GetValue<string>());
        Assert.Equal("Search query", props["query"]?["description"]?.GetValue<string>());
        var required = schema["required"] as JsonArray;
        Assert.NotNull(required);
        Assert.Single(required!);
        Assert.Equal("query", required[0]?.GetValue<string>());
    }

    [Fact]
    public void DeriveSchema_MultipleParamTypes_MapsCorrectly()
    {
        var parameters = new List<ParameterDefinition>
        {
            new() { Name = "name", Type = ParameterType.String },
            new() { Name = "count", Type = ParameterType.Number },
            new() { Name = "active", Type = ParameterType.Boolean },
            new() { Name = "data", Type = ParameterType.Json },
            new() { Name = "items", Type = ParameterType.Array }
        };

        var schema = SchemaDerivation.DeriveSchema(parameters);

        Assert.NotNull(schema);
        var props = schema!["properties"] as JsonObject;
        Assert.NotNull(props);
        Assert.Equal("string", props!["name"]?["type"]?.GetValue<string>());
        Assert.Equal("number", props["count"]?["type"]?.GetValue<string>());
        Assert.Equal("boolean", props["active"]?["type"]?.GetValue<string>());
        Assert.Equal("object", props["data"]?["type"]?.GetValue<string>());
        Assert.Equal("array", props["items"]?["type"]?.GetValue<string>());
    }

    [Fact]
    public void DeriveSchema_WithEnumOptions_AddsEnumArray()
    {
        var parameters = new List<ParameterDefinition>
        {
            new()
            {
                Name = "method",
                Type = ParameterType.Options,
                Options =
                [
                    new Option { Label = "GET", Value = "GET" },
                    new Option { Label = "POST", Value = "POST" }
                ]
            }
        };

        var schema = SchemaDerivation.DeriveSchema(parameters);
        var props = schema?["properties"] as JsonObject;
        var enumArray = props?["method"]?["enum"] as JsonArray;

        Assert.NotNull(enumArray);
        Assert.Equal(2, enumArray!.Count);
        Assert.Equal("GET", enumArray[0]?.GetValue<string>());
        Assert.Equal("POST", enumArray[1]?.GetValue<string>());
    }

    [Fact]
    public void DeriveSchema_WithAiParamPlaceholder_SetsAiParamStructured()
    {
        var parameters = new List<ParameterDefinition>
        {
            new() { Name = "date", Type = ParameterType.String, Description = "{{ai_param:要查询的日期}}" }
        };

        var schema = SchemaDerivation.DeriveSchema(parameters);

        Assert.NotNull(schema);
        Assert.True(schema!["aiParamStructured"]?.GetValue<bool>());
    }

    [Fact]
    public void HasAiParamPlaceholder_WithPlaceholder_ReturnsTrue()
    {
        Assert.True(SchemaDerivation.HasAiParamPlaceholder("{{ai_param:日期}}"));
        Assert.True(SchemaDerivation.HasAiParamPlaceholder("请输入 {{ai_param:查询条件}}"));
    }

    [Fact]
    public void HasAiParamPlaceholder_WithoutPlaceholder_ReturnsFalse()
    {
        Assert.False(SchemaDerivation.HasAiParamPlaceholder("普通文本"));
        Assert.False(SchemaDerivation.HasAiParamPlaceholder(null));
        Assert.False(SchemaDerivation.HasAiParamPlaceholder(""));
    }

    [Fact]
    public void ResolveAiParamDescription_ReplacesPlaceholder()
    {
        var result = SchemaDerivation.ResolveAiParamDescription("请输入 {{ai_param:要查询的日期}}");
        Assert.Equal("请输入 要查询的日期", result);
    }

    [Fact]
    public void ResolveAiParamDescription_NullOrEmpty_ReturnsAsIs()
    {
        Assert.Null(SchemaDerivation.ResolveAiParamDescription(null));
        Assert.Equal("", SchemaDerivation.ResolveAiParamDescription(""));
    }
}
