using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Expressions;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Expressions.Exceptions;
using FlowEngine.Core.Scripting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Jint;

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
    public void Resolve_UrlStringContainingHttp_IsNotBlocked()
    {
        // 字面量中的 "http"/"https" 不应被安全扫描误判为禁止标识符
        using var js = CreateJsEngine();
        var raw = new Dictionary<string, object>
        {
            ["url"] = "\"https://oapi.dingtalk.com/topapi/v2/user/list?access_token=\" + $credentials.testCred.accessToken"
        };

        var result = _resolver.Resolve(raw, js);

        Assert.Equal("https://oapi.dingtalk.com/topapi/v2/user/list?access_token=tok-xxx", result["url"]);
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

    [Fact]
    public void Resolve_DollarInputItem_Evaluates_From_InputContainer()
    {
        // 验证 $input.item() 经 Jint 求值的 camelCase 兼容性
        var data = new JsonObject { ["userid"] = "abc123", ["name"] = "张三" };
        using var js = JsEngine.Create();
        js.SetValue("$json", data);
        js.SetValue("$input", new InputContainer([data], data));
        js.SetValue("$items", new Func<string?, object?>(_ => new List<object?> { data }));
        js.SetValue("$runIndex", 0);
        js.SetValue("$itemIndex", 0);
        js.SetValue("$now", DateTime.UtcNow);
        js.SetValue("$today", DateTime.UtcNow.Date);
        js.SetValue("$node", new Dictionary<string, NodeOutput>(StringComparer.OrdinalIgnoreCase));
        js.SetValue("$workflow", new Dictionary<string, object?>());
        js.SetValue("$execution", new Dictionary<string, object?>());
        js.SetValue("$env", new Dictionary<string, object?>());
        js.SetValue("$vars", new Dictionary<string, object?>());
        js.SetValue("$credentials", new Dictionary<string, object?>());

        var raw = new Dictionary<string, object>
        {
            ["from_item"] = "$input.item().userid",
            ["from_json"] = "$json.name",
        };

        var result = _resolver.Resolve(raw, js);

        Assert.Equal("abc123", result["from_item"]);
        Assert.Equal("张三", result["from_json"]);
    }

    [Fact]
    public void Resolve_DollarCredentials_PropertyAccess_Evaluates_NestedFields()
    {
        // 验证 $credentials.<name>.<field> 经 Jint 属性式访问求值（plan-004 / draft 用法）
        using var js = CreateJsEngine();
        var raw = new Dictionary<string, object>
        {
            ["token"] = "$credentials.testCred.accessToken",
            ["key"] = "$credentials.testCred.apiKey",
        };

        var result = _resolver.Resolve(raw, js);

        Assert.Equal("tok-xxx", result["token"]);
        Assert.Equal("sk-test", result["key"]);
    }

    private static JsEngine CreateJsEngine(JsonObject? inputData = null)
    {
        var js = JsEngine.Create();

        // 旧式裸名（向后兼容）
        var input = inputData ?? new JsonObject();
        js.SetValue("input", input);
        js.SetValue("inputs", new Dictionary<string, DataBatch>());
        js.SetValue("nodes", new Dictionary<string, DataBatch>());
        js.SetValue("items", new Dictionary<string, DataBatch>());
        js.SetValue("workflow", new Dictionary<string, object?>());
        js.SetValue("execution", new Dictionary<string, object?>());
        js.SetValue("runIndex", 0);
        js.SetValue("parameter", new Dictionary<string, object>());
        js.SetValue("env", new Dictionary<string, object?>());

        // $ 前缀内建变量（plan-004 评审5）
        js.SetValue("$json", input);
        var inputItems = new List<object?> { input };
        js.SetValue("$input", new InputContainer(inputItems, input, new Dictionary<string, object>()));
        js.SetValue("$items", new Func<string?, object?>(_ => inputItems));
        js.SetValue("$node", new Dictionary<string, NodeOutput>(StringComparer.OrdinalIgnoreCase));
        js.SetValue("$workflow", new Dictionary<string, object?>());
        js.SetValue("$execution", new Dictionary<string, object?>());
        js.SetValue("$env", new Dictionary<string, object?>());
        js.SetValue("$vars", new Dictionary<string, object?>());
        js.SetValue("$now", DateTime.UtcNow);
        js.SetValue("$today", DateTime.UtcNow.Date);
        js.SetValue("$runIndex", 0);
        js.SetValue("$itemIndex", 0);
        js.SetValue("$credentials", new Dictionary<string, object?>
        {
            ["testCred"] = new Dictionary<string, object?>
            {
                ["apiKey"] = "sk-test",
                ["accessToken"] = "tok-xxx",
            }
        });

        return js;
    }
}
