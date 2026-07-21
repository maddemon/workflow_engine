using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Expressions;
using FlowEngine.Core.Scripting;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Expressions.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Jint;

namespace FlowEngine.Runtime.Tests.Expressions;

public class ParameterResolverTests
{
    private readonly ParameterResolver _resolver;

    public ParameterResolverTests()
    {
        _resolver = new ParameterResolver(
            NullLogger<ParameterResolver>.Instance,
            Microsoft.Extensions.Options.Options.Create(new JsEngineOptions()),
            new ScriptCache(Microsoft.Extensions.Options.Options.Create(new JsEngineOptions())));
    }

    [Fact]
    public async Task Resolve_String_Parameter_Evaluates_Expression()
    {
        using var js = CreateJsEngine(new JsonObject { ["statusCode"] = 200 });
        var raw = new Dictionary<string, object>
        {
            ["condition"] = "input.statusCode === 200"
        };

        var result = await _resolver.ResolveAsync(raw, js);

        Assert.Equal(true, result["condition"]);
    }

    [Fact]
    public async Task Resolve_JsonElement_String_Evaluates_Expression()
    {
        using var js = CreateJsEngine(new JsonObject { ["statusCode"] = 200 });

        var jsonStr = """{"condition": "input.statusCode === 200"}""";
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonStr)!;
        var rawAsObjects = raw.ToDictionary(
            kv => kv.Key,
            kv => (object)kv.Value);

        var result = await _resolver.ResolveAsync(rawAsObjects, js);

        Assert.Equal(true, result["condition"]);
    }

    [Fact]
    public async Task Resolve_JsonElement_NonString_Passes_Through()
    {
        using var js = CreateJsEngine();
        var jsonStr = """{"count": 42}""";
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonStr)!;
        var rawAsObjects = raw.ToDictionary(
            kv => kv.Key,
            kv => (object)kv.Value);

        var result = await _resolver.ResolveAsync(rawAsObjects, js);

        Assert.Equal(42, Convert.ToInt32(result["count"]));
    }

    [Fact]
    public async Task Resolve_Empty_String_Returns_Empty()
    {
        using var js = CreateJsEngine();
        var jsonStr = """{"url": ""}""";
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonStr)!;
        var rawAsObjects = raw.ToDictionary(
            kv => kv.Key,
            kv => (object)kv.Value);

        var result = await _resolver.ResolveAsync(rawAsObjects, js);

        Assert.Equal("", result["url"]);
    }

    [Fact]
    public async Task Resolve_RepeatedCalls_ReturnsConsistentResult()
    {
        // 多次解析同一表达式（含函数调用/缺失字段路径，触发缓存的正则分支）应得到稳定结果。
        using var js = CreateJsEngine(new JsonObject { ["statusCode"] = 200 });
        var raw = new Dictionary<string, object> { ["condition"] = "input.statusCode === 200" };

        for (var i = 0; i < 50; i++)
        {
            var result = await _resolver.ResolveAsync(raw, js);
            Assert.Equal(true, result["condition"]);
        }
    }

    [Fact]
    public async Task Resolve_ForbiddenIdentifier_ThrowsSecurityViolationException()
    {
        using var js = CreateJsEngine();
        var raw = new Dictionary<string, object> { ["expr"] = "eval('1+1')" };

        await Assert.ThrowsAsync<SecurityViolationException>(() => _resolver.ResolveAsync(raw, js));
    }

    [Fact]
    public async Task Resolve_UrlStringContainingHttp_IsNotBlocked()
    {
        // 字面量中的 "http"/"https" 不应被安全扫描误判为禁止标识符
        using var js = CreateJsEngine();
        var raw = new Dictionary<string, object>
        {
            ["url"] = "\"https://oapi.dingtalk.com/topapi/v2/user/list?access_token=\" + $credentials.testCred.accessToken"
        };

        var result = await _resolver.ResolveAsync(raw, js);

        Assert.Equal("https://oapi.dingtalk.com/topapi/v2/user/list?access_token=tok-xxx", result["url"]);
    }

    [Fact]
    public async Task Resolve_InvalidSyntax_ThrowsSyntaxErrorException()
    {
        using var js = CreateJsEngine();
        var raw = new Dictionary<string, object> { ["expr"] = "input.status ===" };

        await Assert.ThrowsAsync<SyntaxErrorException>(() => _resolver.ResolveAsync(raw, js));
    }

    [Fact]
    public async Task Resolve_DollarInputItem_Evaluates_From_InputContainer()
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
        js.SetValue("$credentials", new Dictionary<string, object?>
        {
            ["testCred"] = new Dictionary<string, object?>
            {
                ["apiKey"] = "sk-test",
                ["accessToken"] = "tok-xxx",
            }
        });

        var raw = new Dictionary<string, object>
        {
            ["from_item"] = "$input.item().userid",
            ["from_json"] = "$json.name",
        };

        var result = await _resolver.ResolveAsync(raw, js);

        Assert.Equal("abc123", result["from_item"]);
        Assert.Equal("张三", result["from_json"]);
    }

    [Fact]
    public async Task Resolve_DollarCredentials_PropertyAccess_Evaluates_NestedFields()
    {
        // 验证 $credentials.<name>.<field> 经 Jint 属性式访问求值（plan-004 / draft 用法）
        using var js = CreateJsEngine();
        var raw = new Dictionary<string, object>
        {
            ["token"] = "$credentials.testCred.accessToken",
            ["key"] = "$credentials.testCred.apiKey",
        };

        var result = await _resolver.ResolveAsync(raw, js);

        Assert.Equal("tok-xxx", result["token"]);
        Assert.Equal("sk-test", result["key"]);
    }

    private static JsEngine CreateJsEngine(JsonObject? inputData = null)
    {
        var js = JsEngine.Create();

        // $ 前缀内建变量（plan-004 评审5）
        js.SetValue("$json", inputData);
        js.SetValue("input", inputData);
        var inputItems = new List<object?> { inputData };
        js.SetValue("$input", new InputContainer(inputItems, inputData, new Dictionary<string, object>()));
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

    [Theory]
    [InlineData("page", false)]
    [InlineData("tool", false)]
    [InlineData("node", false)]
    [InlineData("step 1 (see docs)", false)]
    [InlineData("input", false)]
    [InlineData("now", false)]
    [InlineData("42", false)]
    [InlineData("https://example.com", false)]
    [InlineData("input.statusCode", true)]
    [InlineData("$input.item().userid", true)]
    [InlineData("$credentials.testCred.accessToken", true)]
    [InlineData("1 + 2", true)]
    [InlineData("parseInt(\"10\")", true)]
    public async Task Resolve_ExpressionHeuristic_ReturnsExpected(string value, bool shouldEvaluate)
    {
        using var js = CreateJsEngine(new JsonObject { ["statusCode"] = 200 });
        var raw = new Dictionary<string, object> { ["value"] = value };

        var result = await _resolver.ResolveAsync(raw, js);

        if (shouldEvaluate)
        {
            // 表达式应被求值，结果不应等于原字符串。
            Assert.NotEqual(value, result["value"]);
        }
        else
        {
            // 字面量应保持不变。
            Assert.Equal(value, result["value"]);
        }
    }
}
