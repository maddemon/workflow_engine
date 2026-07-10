using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;
using Microsoft.Extensions.Options;

namespace FlowEngine.Core.Tests.Scripting;

public sealed class ScriptResultTests
{
    private static ScriptResult Evaluate(string source, ScriptReturnType returnType = ScriptReturnType.Object)
    {
        var script = new Script { Source = source, ReturnType = returnType };
        var cache = new ScriptCache(Options.Create(new JsEngineOptions()));
        var prepared = cache.GetOrPrepare(script);
        return prepared.RunAsync(ScriptContext.From(new NodeExecutionContext())).GetAwaiter().GetResult();
    }

    [Fact]
    public void ToClr_Undefined_ReturnsNull()
    {
        var result = Evaluate("undefined");

        Assert.Null(result.ToClr());
    }

    [Fact]
    public void ToClr_Boolean_ReturnsBool()
    {
        var result = Evaluate("true");

        Assert.IsType<bool>(result.ToClr());
        Assert.True((bool)result.ToClr()!);
    }

    [Fact]
    public void ToClr_Integer_PreservesInt()
    {
        var result = Evaluate("42");

        Assert.Equal(42, result.To<int>());
    }

    [Fact]
    public void ToClr_Double_PreservesDouble()
    {
        var result = Evaluate("3.14");

        Assert.IsType<double>(result.ToClr());
        Assert.Equal(3.14, result.ToClr());
    }

    [Fact]
    public void ToClr_String_ReturnsString()
    {
        var result = Evaluate("'hello'");

        Assert.Equal("hello", result.ToClr());
    }

    [Fact]
    public void ToClr_Object_ReturnsJsonObject()
    {
        var result = Evaluate("({a:1,b:'two'})");

        var clr = result.ToClr();

        Assert.IsType<JsonObject>(clr);
        var obj = (JsonObject)clr;
        Assert.Equal(1, obj["a"]!.GetValue<int>());
        Assert.Equal("two", obj["b"]!.GetValue<string>());
    }

    [Fact]
    public void ToClr_Array_ReturnsJsonArray()
    {
        var result = Evaluate("[1, 2, 3]");

        var clr = result.ToClr();

        Assert.IsType<JsonArray>(clr);
        var arr = (JsonArray)clr;
        Assert.Equal(3, arr.Count);
    }

    [Fact]
    public void ToBoolean_FalsyValues_AreFalse()
    {
        Assert.False(Evaluate("false").ToBoolean());
        Assert.False(Evaluate("0").ToBoolean());
        Assert.False(Evaluate("''").ToBoolean());
        Assert.False(Evaluate("null").ToBoolean());
        Assert.False(Evaluate("undefined").ToBoolean());
        Assert.False(Evaluate("NaN").ToBoolean());
    }

    [Fact]
    public void ToBoolean_TruthyValues_AreTrue()
    {
        Assert.True(Evaluate("true").ToBoolean());
        Assert.True(Evaluate("1").ToBoolean());
        Assert.True(Evaluate("'x'").ToBoolean());
        Assert.True(Evaluate("[]").ToBoolean());
        Assert.True(Evaluate("{}").ToBoolean());
    }

    [Fact]
    public void ToJson_Object_ReturnsJsonObject()
    {
        var result = Evaluate("({a:1,b:'two'})");

        var json = result.ToJson() as JsonObject;

        Assert.NotNull(json);
        Assert.Equal(1, json["a"]!.GetValue<int>());
        Assert.Equal("two", json["b"]!.GetValue<string>());
    }

    [Fact]
    public void To_TypedConversions_Work()
    {
        Assert.Equal("hello", Evaluate("'hello'").To<string>());
        Assert.True(Evaluate("true").To<bool>());
        Assert.Equal(42, Evaluate("42").To<int>());
        Assert.Equal(3.14, Evaluate("3.14").To<double>());
    }



    [Fact]
    public void To_Dictionary_ReturnsStringDictionary()
    {
        var result = Evaluate("({a:1,b:2})", ScriptReturnType.Dictionary);

        var dict = result.To<Dictionary<string, string>>();

        Assert.NotNull(dict);
        Assert.Equal("1", dict["a"]);
        Assert.Equal("2", dict["b"]);
    }

    [Fact]
    public void FailedResult_ThrowsScriptErrorExceptionOnConversion()
    {
        var script = new Script { Source = "x" };
        var error = new ScriptErrorException(script, "boom");
        var result = new ScriptResult(script, error);

        Assert.Throws<ScriptErrorException>(() => result.ToClr());
        Assert.Throws<ScriptErrorException>(() => result.ToBoolean());
        Assert.Throws<ScriptErrorException>(() => result.ToJson());
        Assert.Throws<ScriptErrorException>(() => result.To<int>());
    }
}
