using System.Text.Json.Nodes;
using FlowEngine.Runtime.Scripting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests.Scripting;

public class JsEngineSecurityTests
{
    [Fact]
    public void Evaluate_Now_Returns_DateString()
    {
        using var js = JsEngine.Create();
        var result = JsEngine.ToClrValue(js.Evaluate("now()"));
        Assert.NotNull(result);
        Assert.IsType<string>(result);
        Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}", (string)result);
    }

    [Fact]
    public void Evaluate_NowIso_Returns_IsoString()
    {
        using var js = JsEngine.Create();
        var result = JsEngine.ToClrValue(js.Evaluate("nowIso()"));
        var str = Assert.IsType<string>(result);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", str);
    }

    [Fact]
    public void Evaluate_Jmespath_SimpleProperty_ReturnsJsonString()
    {
        using var js = JsEngine.Create();
        var data = new JsonObject { ["name"] = "test" };
        js.SetValue("data", data);

        var result = JsEngine.ToClrValue(js.Evaluate(@"jmespath(data, ""name"")"));
        Assert.Equal("\"test\"", result);
    }

    [Fact]
    public void Evaluate_Jmespath_NestedProperty_ReturnsJsonString()
    {
        using var js = JsEngine.Create();
        var data = new JsonObject
        {
            ["user"] = new JsonObject { ["profile"] = new JsonObject { ["age"] = 30 } }
        };
        js.SetValue("data", data);

        var result = JsEngine.ToClrValue(js.Evaluate(@"jmespath(data, ""user.profile.age"")"));
        Assert.Equal("30", result);
    }

    [Fact]
    public void Evaluate_Jmespath_ArrayIndex_ReturnsJsonString()
    {
        using var js = JsEngine.Create();
        var data = new JsonObject
        {
            ["items"] = new JsonArray { "a", "b", "c" }
        };
        js.SetValue("data", data);

        var result = JsEngine.ToClrValue(js.Evaluate(@"jmespath(data, ""items[1]"")"));
        Assert.Equal("\"b\"", result);
    }

    [Fact]
    public void Evaluate_Jmespath_NullData_ReturnsNull()
    {
        using var js = JsEngine.Create();
        js.SetValue("data", null);

        var result = JsEngine.ToClrValue(js.Evaluate(@"jmespath(data, ""name"")"));
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_Jmespath_NonexistentPath_ReturnsNull()
    {
        using var js = JsEngine.Create();
        var data = new JsonObject { ["name"] = "test" };
        js.SetValue("data", data);

        var result = JsEngine.ToClrValue(js.Evaluate(@"jmespath(data, ""nonexistent"")"));
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_StringLength_Works_Natively()
    {
        using var js = JsEngine.Create();
        js.SetValue("s", "hello");
        var result = JsEngine.ToClrValue(js.Evaluate("s.length"));
        Assert.Equal(5, Convert.ToInt32(result));
    }

    [Fact]
    public void Evaluate_Trim_Works_Natively()
    {
        using var js = JsEngine.Create();
        js.SetValue("s", "  hello  ");
        var result = JsEngine.ToClrValue(js.Evaluate("s.trim()"));
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Evaluate_Arithmetic_Works()
    {
        using var js = JsEngine.Create();
        js.SetValue("a", 10);
        js.SetValue("b", 20);
        var result = JsEngine.ToClrValue(js.Evaluate("a + b"));
        Assert.Equal(30d, result);
    }

    [Fact]
    public void Evaluate_Comparison_Works()
    {
        using var js = JsEngine.Create();
        js.SetValue("x", 42);
        var result = JsEngine.ToClrValue(js.Evaluate("x > 40"));
        Assert.True((bool)result!);
    }

    [Fact]
    public void Evaluate_ConsoleLog_Does_Not_Throw()
    {
        using var js = JsEngine.Create(logger: NullLogger<JsEngine>.Instance);
        js.SetValue("msg", "test");
        var ex = Record.Exception(() => js.Evaluate("console.log(msg)"));
        Assert.Null(ex);
    }

    [Fact]
    public void Evaluate_ConsoleWarn_Does_Not_Throw()
    {
        using var js = JsEngine.Create(logger: NullLogger<JsEngine>.Instance);
        var ex = Record.Exception(() => js.Evaluate("console.warn('test')"));
        Assert.Null(ex);
    }
}
