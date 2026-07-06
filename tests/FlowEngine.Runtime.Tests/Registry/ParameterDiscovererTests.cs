using System.ComponentModel;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Registry;

namespace FlowEngine.Runtime.Tests.Registry;

public class ParameterDiscovererTests
{
    private class ComplexItem
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    private class ArrayNode : INodeType
    {
        public string TypeName => "arrayNode";
        public string DisplayName => "Array Node";
        public string Category => "Core";
        public string Icon => "array";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports { get; } = [];
        public bool DefaultIsEntry => false;

        [Description("Fields to set.")]
        public List<ComplexItem> Fields { get; set; } = [];

        public List<string> Tags { get; set; } = [];

        public Task<NodeExecutionResult> ExecuteAsync(
            NodeExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new NodeExecutionResult { Success = true });
        }
    }

    [Fact]
    public void Discover_Array_Of_ComplexType_Generates_ItemDefinition()
    {
        var discoverer = new ParameterDiscoverer();

        var parameters = discoverer.Discover(typeof(ArrayNode));

        var fieldsParam = parameters.Single(p => p.Name == "fields");
        Assert.Equal(ParameterType.Array, fieldsParam.Type);
        Assert.NotNull(fieldsParam.ItemDefinition);
        Assert.Equal(ParameterType.Json, fieldsParam.ItemDefinition.Type);
        Assert.Equal(2, fieldsParam.ItemDefinition.Fields.Count);
        Assert.Contains(fieldsParam.ItemDefinition.Fields, f => f.Name == "name" && f.Type == ParameterType.String);
        Assert.Contains(fieldsParam.ItemDefinition.Fields, f => f.Name == "value" && f.Type == ParameterType.String);
    }

    [Fact]
    public void Discover_Array_Of_String_Does_Not_Generate_ItemDefinition()
    {
        var discoverer = new ParameterDiscoverer();

        var parameters = discoverer.Discover(typeof(ArrayNode));

        var tagsParam = parameters.Single(p => p.Name == "tags");
        Assert.Equal(ParameterType.Array, tagsParam.Type);
        Assert.Null(tagsParam.ItemDefinition);
    }
}
