using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Expressions;
using FlowEngine.Core.Scripting;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Expressions.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Jint;

namespace FlowEngine.Runtime.Tests.Expressions;

/// <summary>
/// <see cref="ParameterResolver"/> 异常路径与 JsonElement 转换覆盖测试。
/// 正常路径、语法错误、安全违规已由 <c>ParameterResolverTests</c> / <c>ParameterResolverSecurityTests</c> 覆盖，
/// 此处聚焦 FieldNotFound / TypeMismatch 异常映射，以及 JsonElement 各类别的转换分支。
/// </summary>
public class ParameterResolverExceptionsTests
{
    private readonly ParameterResolver _resolver = new(
        NullLogger<ParameterResolver>.Instance,
        Microsoft.Extensions.Options.Options.Create(new JsEngineOptions()),
        new ScriptCache(Microsoft.Extensions.Options.Options.Create(new JsEngineOptions())));

    [Fact]
    public async Task Resolve_FieldNotFound_ThrowsFieldNotFoundException()
    {
        using var js = CreateJsEngine();
        // $json.x 未定义 → 访问 (undefined).y 抛 "Cannot read properties of undefined (reading 'y')"
        // → 映射为 FieldNotFoundException。注意：$json.a.b（a 为数字）在 JS 中返回 undefined 不抛错。
        var raw = new Dictionary<string, object> { ["expr"] = "$json.x.y" };

        await Assert.ThrowsAsync<FieldNotFoundException>(() => _resolver.ResolveAsync(raw, js));
    }

    [Fact]
    public async Task Resolve_TypeMismatch_ThrowsTypeMismatchException()
    {
        using var js = CreateJsEngine();
        var raw = new Dictionary<string, object> { ["expr"] = "$json.a()" };

        await Assert.ThrowsAsync<TypeMismatchException>(() => _resolver.ResolveAsync(raw, js));
    }

    [Fact]
    public async Task Resolve_JsonElement_VariousKinds_ConvertedAppropriately()
    {
        using var js = CreateJsEngine();
        var raw = new Dictionary<string, object>
        {
            ["num"] = JsonSerializer.Deserialize<JsonElement>("3.5"),
            ["t"] = JsonSerializer.Deserialize<JsonElement>("true"),
            ["f"] = JsonSerializer.Deserialize<JsonElement>("false"),
            ["nul"] = JsonSerializer.Deserialize<JsonElement>("null"),
            ["obj"] = JsonSerializer.Deserialize<JsonElement>("{\"x\":1}"),
            ["arr"] = JsonSerializer.Deserialize<JsonElement>("[1,2]"),
        };

        var result = await _resolver.ResolveAsync(raw, js);

        Assert.IsType<decimal>(result["num"]);
        Assert.Equal(3.5m, (decimal)result["num"]);
        Assert.Equal(true, result["t"]);
        Assert.Equal(false, result["f"]);
        Assert.Null(result["nul"]);
        Assert.IsType<string>(result["obj"]);
        Assert.Contains("\"x\"", (string)result["obj"]);
        Assert.IsType<string>(result["arr"]);
        Assert.Contains("1", (string)result["arr"]);
    }

    [Fact]
    public async Task Resolve_NestedDictionary_ResolvesInnerExpressions()
    {
        using var js = CreateJsEngine();
        var raw = new Dictionary<string, object>
        {
            ["map"] = new Dictionary<string, object> { ["k"] = "$json.a" }
        };

        var result = await _resolver.ResolveAsync(raw, js);

        var resolvedMap = Assert.IsType<Dictionary<string, object>>(result["map"]);
        Assert.Equal(1, Convert.ToInt32(resolvedMap["k"]));
    }

    [Fact]
    public async Task Resolve_NestedList_ResolvesInnerExpressions()
    {
        using var js = CreateJsEngine();
        var raw = new Dictionary<string, object>
        {
            ["list"] = new List<object> { "$json.a", "$json.b" }
        };

        var result = await _resolver.ResolveAsync(raw, js);

        var resolvedList = Assert.IsType<List<object>>(result["list"]);
        Assert.Equal(2, resolvedList.Count);
        Assert.Equal(1, Convert.ToInt32(resolvedList[0]));
        Assert.Equal(2, Convert.ToInt32(resolvedList[1]));
    }

    private static JsEngine CreateJsEngine()
    {
        var js = JsEngine.Create();
        js.SetValue("$json", new JsonObject { ["a"] = 1, ["b"] = 2 });
        return js;
    }
}
