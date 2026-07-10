using System.Text.Json.Nodes;
using FlowEngine.Core.Scripting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests.Scripting;

public class JsEngineSecurityTests
{
    [Fact]
    public void Evaluate_Now_Returns_DateString()
    {
        using var js = JsEngine.Create();
        var result = new ScriptResult(Script.Empty, js.Evaluate("now()")).ToClr();
        Assert.NotNull(result);
        Assert.IsType<string>(result);
        Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}", (string)result);
    }

    [Fact]
    public void Evaluate_NowIso_Returns_IsoString()
    {
        using var js = JsEngine.Create();
        var result = new ScriptResult(Script.Empty, js.Evaluate("nowIso()")).ToClr();
        var str = Assert.IsType<string>(result);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", str);
    }

    [Fact]
    public void Evaluate_Jmespath_SimpleProperty_ReturnsJsonString()
    {
        using var js = JsEngine.Create();
        var data = new JsonObject { ["name"] = "test" };
        js.SetValue("data", data);

        var result = new ScriptResult(Script.Empty, js.Evaluate(@"jmespath(data, ""name"")")).ToClr();
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

        var result = new ScriptResult(Script.Empty, js.Evaluate(@"jmespath(data, ""user.profile.age"")")).ToClr();
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

        var result = new ScriptResult(Script.Empty, js.Evaluate(@"jmespath(data, ""items[1]"")")).ToClr();
        Assert.Equal("\"b\"", result);
    }

    [Fact]
    public void Evaluate_Jmespath_NullData_ReturnsNull()
    {
        using var js = JsEngine.Create();
        js.SetValue("data", null);

        var result = new ScriptResult(Script.Empty, js.Evaluate(@"jmespath(data, ""name"")")).ToClr();
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_Jmespath_NonexistentPath_ReturnsNull()
    {
        using var js = JsEngine.Create();
        var data = new JsonObject { ["name"] = "test" };
        js.SetValue("data", data);

        var result = new ScriptResult(Script.Empty, js.Evaluate(@"jmespath(data, ""nonexistent"")")).ToClr();
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_StringLength_Works_Natively()
    {
        using var js = JsEngine.Create();
        js.SetValue("s", "hello");
        var result = new ScriptResult(Script.Empty, js.Evaluate("s.length")).ToClr();
        Assert.Equal(5, Convert.ToInt32(result));
    }

    [Fact]
    public void Evaluate_Trim_Works_Natively()
    {
        using var js = JsEngine.Create();
        js.SetValue("s", "  hello  ");
        var result = new ScriptResult(Script.Empty, js.Evaluate("s.trim()")).ToClr();
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Evaluate_Arithmetic_Works()
    {
        using var js = JsEngine.Create();
        js.SetValue("a", 10);
        js.SetValue("b", 20);
        var result = new ScriptResult(Script.Empty, js.Evaluate("a + b")).ToClr();
        Assert.Equal(30d, result);
    }

    [Fact]
    public void Evaluate_Comparison_Works()
    {
        using var js = JsEngine.Create();
        js.SetValue("x", 42);
        var result = new ScriptResult(Script.Empty, js.Evaluate("x > 40")).ToClr();
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
