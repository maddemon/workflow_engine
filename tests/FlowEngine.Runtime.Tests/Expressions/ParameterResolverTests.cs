using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Expressions;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Expressions.Exceptions;
using FlowEngine.Core.Scripting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests.Expressions;

public class ParameterResolverTests
{
    private readonly ParameterResolver _resolver;

    public ParameterResolverTests()
    {
        _resolver = new ParameterResolver(NullLogger<ParameterResolver>.Instance);
    }

    [Fact]
    public void Resolve_String_Parameter_Evaluates_Expression()
    {
        using var js = CreateJsEngine(new JsonObject { ["statusCode"] = 200 });
        var raw = new Dictionary<string, object>
        {
            ["condition"] = "input.statusCode === 200"
        };

        var result = _resolver.Resolve(raw, js);

        Assert.Equal(true, result["condition"]);
    }

    [Fact]
    public void Resolve_JsonElement_String_Evaluates_Expression()
    {
        using var js = CreateJsEngine(new JsonObject { ["statusCode"] = 200 });

        var jsonStr = """{"condition": "input.statusCode === 200"}""";
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonStr)!;
        var rawAsObjects = raw.ToDictionary(
            kv => kv.Key,
            kv => (object)kv.Value);

        var result = _resolver.Resolve(rawAsObjects, js);

        Assert.Equal(true, result["condition"]);
    }

    [Fact]
    public void Resolve_JsonElement_NonString_Passes_Through()
    {
        using var js = CreateJsEngine();
        var jsonStr = """{"count": 42}""";
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonStr)!;
        var rawAsObjects = raw.ToDictionary(
            kv => kv.Key,
            kv => (object)kv.Value);

        var result = _resolver.Resolve(rawAsObjects, js);

        Assert.Equal(42, Convert.ToInt32(result["count"]));
    }

    [Fact]
    public void Resolve_Empty_String_Returns_Empty()
    {
        using var js = CreateJsEngine();
        var jsonStr = """{"url": ""}""";
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonStr)!;
        var rawAsObjects = raw.ToDictionary(
            kv => kv.Key,
            kv => (object)kv.Value);

        var result = _resolver.Resolve(rawAsObjects, js);

        Assert.Equal("", result["url"]);
    }

    [Fact]
    public void Resolve_ForbiddenIdentifier_ThrowsSecurityViolationException()
    {
        using var js = CreateJsEngine();
        var raw = new Dictionary<string, object> { ["expr"] = "eval('1+1')" };

        Assert.Throws<SecurityViolationException>(() => _resolver.Resolve(raw, js));
    }

    [Fact]
    public void Resolve_InvalidSyntax_ThrowsSyntaxErrorException()
    {
        using var js = CreateJsEngine();
        var raw = new Dictionary<string, object> { ["expr"] = "input.status ===" };

        Assert.Throws<SyntaxErrorException>(() => _resolver.Resolve(raw, js));
    }



    [Fact]
    public void Resolve_WithCacheKey_CachesPreparedScript()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new ParameterResolver(NullLogger<ParameterResolver>.Instance, cache);
        using var js = CreateJsEngine(new JsonObject { ["value"] = 2 });
        var raw = new Dictionary<string, object> { ["expr"] = "input.value * 2" };
        var cacheKey = new ExpressionCacheKey(string.Empty, "schema-a", "schema-b");

        var first = resolver.Resolve(raw, js, cacheKey);
        var second = resolver.Resolve(raw, js, cacheKey);

        Assert.Equal(4, Convert.ToInt32(first["expr"]));
        Assert.Equal(4, Convert.ToInt32(second["expr"]));
        Assert.True(cache.TryGetValue(cacheKey with { Expression = "input.value * 2" }, out _));
    }

    private static JsEngine CreateJsEngine(JsonObject? inputData = null)
    {
        var js = JsEngine.Create();
        js.SetValue("input", inputData ?? new JsonObject());
        js.SetValue("inputs", new Dictionary<string, DataBatch>());
        js.SetValue("nodes", new Dictionary<string, DataBatch>());
        js.SetValue("items", new Dictionary<string, DataBatch>());
        js.SetValue("workflow", new Dictionary<string, object?>());
        js.SetValue("execution", new Dictionary<string, object?>());
        js.SetValue("runIndex", 0);
        js.SetValue("parameter", new Dictionary<string, object>());
        js.SetValue("env", new Dictionary<string, object?>());
        return js;
    }
}
