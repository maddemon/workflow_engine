using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests.Registry;

/// <summary>
/// 参数注入器测试 —— 覆盖 JsonElement 类型属性赋值。
/// </summary>
public class ParameterHydratorTests
{
    private readonly ParameterHydrator _hydrator = new();

    [Fact]
    public async Task Hydrate_Empty_String_JsonElement_Sets_Null_For_JsonObject_Property()
    {
        var node = new HttpRequestNode();
        var jsonStr = """{"body": ""}""";
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonStr)!;
        var resolved = raw.ToDictionary(kv => kv.Key, kv => (object)kv.Value);

        await _hydrator.HydrateAsync(node, resolved);

        Assert.Null(node.BodyExpression);
    }

    [Fact]
    public async Task Hydrate_Valid_JsonElement_Sets_String_Property()
    {
        var node = new HttpRequestNode();
        var jsonStr = """{"bodyExpression": "{'key': 'value'}"}""";
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonStr)!;
        var resolved = raw.ToDictionary(kv => kv.Key, kv => (object)kv.Value);

        await _hydrator.HydrateAsync(node, resolved);

        Assert.NotNull(node.BodyExpression);
        Assert.Equal("{'key': 'value'}", node.BodyExpression.Source);
    }

    [Fact]
    public async Task Hydrate_String_Value_Sets_String_Property()
    {
        var node = new HttpRequestNode();
        var resolved = new Dictionary<string, object>
        {
            ["url"] = "https://example.com",
            ["method"] = "Get"
        };

        await _hydrator.HydrateAsync(node, resolved);

        Assert.Equal("https://example.com", node.Url.Source);
        Assert.Equal(HttpMethodOption.Get, node.Method);
    }

    [Fact]
    public async Task Hydrate_JsonElement_Url_Sets_String_Property()
    {
        var node = new HttpRequestNode();
        var jsonStr = """{"url": "https://example.com"}""";
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonStr)!;
        var resolved = raw.ToDictionary(kv => kv.Key, kv => (object)kv.Value);

        await _hydrator.HydrateAsync(node, resolved);

        Assert.Equal("https://example.com", node.Url.Source);
    }

    [Fact]
    public async Task Hydrate_String_To_Script_Sets_SourceAndDefaults()
    {
        var node = new ScriptTestNode();
        var resolved = new Dictionary<string, object>
        {
            ["expression"] = "$json.value > 0"
        };

        await _hydrator.HydrateAsync(node, resolved);

        Assert.NotNull(node.Expression);
        Assert.Equal("$json.value > 0", node.Expression.Source);
        Assert.Equal(ScriptLanguage.JavaScript, node.Expression.Language);
        Assert.Equal(ScriptReturnType.String, node.Expression.ReturnType);
    }

    [Fact]
    public async Task Hydrate_JsonElement_To_Script_Deserializes()
    {
        var node = new ScriptTestNode();
        var jsonStr = """{"expression": {"source": "$json.value > 0", "returnType": "bool"}}""";
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonStr)!;
        var resolved = raw.ToDictionary(kv => kv.Key, kv => (object)kv.Value);

        await _hydrator.HydrateAsync(node, resolved);

        Assert.NotNull(node.Expression);
        Assert.Equal("$json.value > 0", node.Expression.Source);
        Assert.Equal(ScriptReturnType.Bool, node.Expression.ReturnType);
    }

    [Fact]
    public async Task Hydrate_JsonNode_To_DictionaryOfScript_Deserializes()
    {
        var node = new ScriptTestNode();
        var jsonStr = """{"mappings": {"a": {"source": "1 + 1"}, "b": {"source": "2 + 2"}}}""";
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonNode>>(jsonStr)!;
        var resolved = raw.ToDictionary(kv => kv.Key, kv => (object)kv.Value);

        await _hydrator.HydrateAsync(node, resolved);

        Assert.NotNull(node.Mappings);
        Assert.Equal(2, node.Mappings.Count);
        Assert.Equal("1 + 1", node.Mappings["a"].Source);
        Assert.Equal("2 + 2", node.Mappings["b"].Source);
    }

    [Fact]
    public async Task Hydrate_RangeAttribute_Clamps_NumericValue_ToInterval()
    {
        var node = new RangeNode();
        var resolved = new Dictionary<string, object>
        {
            ["maxItems"] = 50,
            ["minItems"] = -5,
            ["kept"] = 7
        };

        await _hydrator.HydrateAsync(node, resolved);

        Assert.Equal(10, node.MaxItems);   // 50 被 clamp 到 [1,10]
        Assert.Equal(1, node.MinItems);    // -5 被 clamp 到 [1,100]
        Assert.Equal(7, node.Kept);        // 区间内的原值保持不变
    }

    [Fact]
    public async Task Hydrate_NullableRangeAttribute_Clamps_NullableValue()
    {
        var node = new RangeNode();
        var resolved = new Dictionary<string, object>
        {
            ["optional"] = 999
        };

        await _hydrator.HydrateAsync(node, resolved);

        Assert.Equal(100, node.Optional);  // 999 被 clamp 到 [0,100]
    }

    private class ScriptTestNode : INodeType
    {
        public string TypeName => "scriptTest";
        public string DisplayName => "Script Test";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports { get; } = [];
        public bool DefaultIsEntry => false;
        public Script Expression { get; set; } = Script.Empty;
        public Dictionary<string, Script> Mappings { get; set; } = [];
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });
    }

    private class RangeNode : INodeType
    {
        public string TypeName => "rangeTest";
        public string DisplayName => "Range Test";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports { get; } = [];
        public bool DefaultIsEntry => false;
        [Range(1, 10)] public int MaxItems { get; set; } = 3;
        [Range(1, 100)] public int MinItems { get; set; } = 5;
        [Range(0, 100)] public int Optional { get; set; }
        [Range(1, 20)] public int Kept { get; set; } = 1;
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });
    }
}
