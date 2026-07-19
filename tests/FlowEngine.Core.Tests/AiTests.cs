using System.Text.Json.Nodes;
using FlowEngine.Core.Ai;

namespace FlowEngine.Core.Tests;

public class AiTests
{
    [Fact]
    public void AiNodeDefinition_Properties_RoundTrip()
    {
        var def = new AiNodeDefinition
        {
            Name = "http",
            DisplayName = "HTTP Request",
            Description = "desc",
            Category = "network",
            Tags = ["api"],
            IsTrigger = false,
            InputSchema = JsonNode.Parse("{}"),
            OutputSchema = JsonNode.Parse("{}"),
            Ports = [new AiPortSchema { Name = "input" }],
            Examples = [new AiExample { Description = "ex" }]
        };

        Assert.Equal("http", def.Name);
        Assert.Equal("HTTP Request", def.DisplayName);
        Assert.Equal("desc", def.Description);
        Assert.Equal("network", def.Category);
        Assert.Single(def.Tags);
        Assert.False(def.IsTrigger);
        Assert.NotNull(def.InputSchema);
        Assert.NotNull(def.OutputSchema);
        Assert.Single(def.Ports);
        Assert.Single(def.Examples);
        Assert.Equal("javascript", def.ExpressionLanguage);
    }

    [Fact]
    public void AiExample_Properties_RoundTrip()
    {
        var example = new AiExample
        {
            Description = "example",
            Input = JsonValue.Create("in"),
            Output = JsonValue.Create("out")
        };

        Assert.Equal("example", example.Description);
        Assert.NotNull(example.Input);
        Assert.NotNull(example.Output);
    }

    [Fact]
    public void AiNodeSummary_Properties_RoundTrip()
    {
        var summary = new AiNodeSummary
        {
            Name = "http",
            DisplayName = "HTTP Request",
            Description = "desc",
            Category = "network",
            Tags = ["api"],
            IsTrigger = true
        };

        Assert.Equal("http", summary.Name);
        Assert.Equal("HTTP Request", summary.DisplayName);
        Assert.Equal("desc", summary.Description);
        Assert.Equal("network", summary.Category);
        Assert.Single(summary.Tags);
        Assert.True(summary.IsTrigger);
    }

    [Fact]
    public void AiPortSchema_Properties_RoundTrip()
    {
        var port = new AiPortSchema
        {
            Name = "input",
            Direction = "Input",
            Description = "desc",
            Type = "Main"
        };

        Assert.Equal("input", port.Name);
        Assert.Equal("Input", port.Direction);
        Assert.Equal("desc", port.Description);
        Assert.Equal("Main", port.Type);
    }

    [Fact]
    public void AiDefinitionHelpers_Def_CreatesDefinition()
    {
        var outputSchema = JsonNode.Parse("{\"type\":\"object\"}");
        var example = AiDefinitionHelpers.Example("ex", JsonValue.Create("in"), JsonValue.Create("out"));

        var def = AiDefinitionHelpers.Def(
            "HTTP Request",
            "network",
            false,
            "Makes HTTP request",
            ["api", "http"],
            outputSchema,
            example);

        Assert.Equal("HTTP Request", def.DisplayName);
        Assert.Equal("network", def.Category);
        Assert.False(def.IsTrigger);
        Assert.Equal("Makes HTTP request", def.Description);
        Assert.Equal(["api", "http"], def.Tags);
        Assert.Same(outputSchema, def.OutputSchema);
        Assert.Single(def.Examples);
    }

    [Fact]
    public void AiDefinitionHelpers_Example_CreatesExample()
    {
        var example = AiDefinitionHelpers.Example("ex", JsonValue.Create("in"), JsonValue.Create("out"));

        Assert.Equal("ex", example.Description);
        Assert.NotNull(example.Input);
        Assert.NotNull(example.Output);
    }
}
