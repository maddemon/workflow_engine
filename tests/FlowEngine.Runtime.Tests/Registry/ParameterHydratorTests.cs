using System.Text.Json;
using System.Text.Json.Nodes;
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

        Assert.Null(node.Body);
    }

    [Fact]
    public async Task Hydrate_Valid_JsonElement_Sets_JsonObject_Property()
    {
        var node = new HttpRequestNode();
        var jsonStr = """{"body": {"key": "value"}}""";
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonStr)!;
        var resolved = raw.ToDictionary(kv => kv.Key, kv => (object)kv.Value);

        await _hydrator.HydrateAsync(node, resolved);

        Assert.NotNull(node.Body);
        Assert.Equal("value", node.Body!["key"]?.ToString());
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

        Assert.Equal("https://example.com", node.Url);
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

        Assert.Equal("https://example.com", node.Url);
    }
}
