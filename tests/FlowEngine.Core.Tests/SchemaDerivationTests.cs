using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Tools;

namespace FlowEngine.Core.Tests;

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
    public void DeriveSchema_StringParameter_BuildsSchema()
    {
        var parameters = new List<ParameterDefinition>
        {
            new() { Name = "text", DisplayName = "Text", Type = ParameterType.String, Required = true }
        };

        var result = SchemaDerivation.DeriveSchema(parameters);

        Assert.NotNull(result);
        Assert.Equal("object", result!["type"]!.GetValue<string>());
        Assert.Equal("string", result["properties"]!["text"]!["type"]!.GetValue<string>());
        Assert.Equal("Text", result["properties"]!["text"]!["description"]!.GetValue<string>());
        Assert.Contains("text", result["required"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void DeriveSchema_NumberAndBooleanParameters_BuildsSchema()
    {
        var parameters = new List<ParameterDefinition>
        {
            new() { Name = "count", DisplayName = "Count", Type = ParameterType.Number },
            new() { Name = "active", DisplayName = "Active", Type = ParameterType.Boolean }
        };

        var result = SchemaDerivation.DeriveSchema(parameters);

        Assert.Equal("number", result!["properties"]!["count"]!["type"]!.GetValue<string>());
        Assert.Equal("boolean", result["properties"]!["active"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void DeriveSchema_ParameterWithOptions_BuildsEnumSchema()
    {
        var parameters = new List<ParameterDefinition>
        {
            new()
            {
                Name = "choice",
                DisplayName = "Choice",
                Type = ParameterType.String,
                Options =
                [
                    new Option { Label = "A", Value = "a" },
                    new Option { Label = "B", Value = "b" }
                ]
            }
        };

        var result = SchemaDerivation.DeriveSchema(parameters);

        var enumArray = result!["properties"]!["choice"]!["enum"]!.AsArray();
        Assert.Equal(2, enumArray.Count);
        Assert.Contains("a", enumArray.Select(n => n!.GetValue<string>()));
        Assert.Contains("b", enumArray.Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void DeriveSchema_ArrayParameter_WithItemDefinition_BuildsItemsSchema()
    {
        var parameters = new List<ParameterDefinition>
        {
            new()
            {
                Name = "items",
                DisplayName = "Items",
                Type = ParameterType.Array,
                ItemDefinition = new ParameterDefinition
                {
                    Name = "item",
                    DisplayName = "Item",
                    Type = ParameterType.String
                }
            }
        };

        var result = SchemaDerivation.DeriveSchema(parameters);

        Assert.Equal("array", result!["properties"]!["items"]!["type"]!.GetValue<string>());
        Assert.Equal("string", result["properties"]!["items"]!["items"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void DeriveSchema_JsonParameter_WithFields_BuildsPropertiesSchema()
    {
        var parameters = new List<ParameterDefinition>
        {
            new()
            {
                Name = "config",
                DisplayName = "Config",
                Type = ParameterType.Json,
                Fields =
                [
                    new ParameterDefinition { Name = "host", DisplayName = "Host", Type = ParameterType.String },
                    new ParameterDefinition { Name = "port", DisplayName = "Port", Type = ParameterType.Number }
                ]
            }
        };

        var result = SchemaDerivation.DeriveSchema(parameters);

        Assert.Equal("object", result!["properties"]!["config"]!["type"]!.GetValue<string>());
        Assert.Equal("string", result["properties"]!["config"]!["properties"]!["host"]!["type"]!.GetValue<string>());
        Assert.Equal("number", result["properties"]!["config"]!["properties"]!["port"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void DeriveSchema_AiParamPlaceholder_SetsFlag()
    {
        var parameters = new List<ParameterDefinition>
        {
            new() { Name = "prompt", DisplayName = "Prompt", Type = ParameterType.String, Description = "Use {{ai_param:custom field}}" }
        };

        var result = SchemaDerivation.DeriveSchema(parameters);

        Assert.True(result!["aiParamStructured"]!.GetValue<bool>());
    }

    [Fact]
    public void DeriveSchema_NoRequiredParameters_OmitsRequired()
    {
        var parameters = new List<ParameterDefinition>
        {
            new() { Name = "optional", DisplayName = "Optional", Type = ParameterType.String, Required = false }
        };

        var result = SchemaDerivation.DeriveSchema(parameters);

        Assert.Null(result!["required"]);
    }

    [Fact]
    public void ResolveAiParamDescription_WithPlaceholder_ReplacesWithDescription()
    {
        var result = SchemaDerivation.ResolveAiParamDescription("Use {{ai_param:custom field}} here");

        Assert.Equal("Use custom field here", result);
    }

    [Fact]
    public void ResolveAiParamDescription_Null_ReturnsNull()
    {
        var result = SchemaDerivation.ResolveAiParamDescription(null);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveAiParamDescription_Empty_ReturnsEmpty()
    {
        var result = SchemaDerivation.ResolveAiParamDescription(string.Empty);

        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData("{{ai_param:desc}}", true)]
    [InlineData("plain text", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasAiParamPlaceholder_DetectsPlaceholder(string? text, bool expected)
    {
        Assert.Equal(expected, SchemaDerivation.HasAiParamPlaceholder(text));
    }
}
