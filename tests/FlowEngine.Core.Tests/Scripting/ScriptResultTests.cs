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

    [Fact]
    public void ToClr_LargeIntegerViaResolvedNode_PreservesLong()
    {
        var script = new Script { Source = "x" }.WithResolvedValue(JsonNode.Parse("9007199254740993"));
        var result = ScriptResult.FromResolved(script);

        var clr = result.ToClr();

        Assert.IsType<long>(clr);
        Assert.Equal(9007199254740993L, (long)clr!);
    }

    [Fact]
    public void ToJson_LargeIntegerViaResolvedNode_EmitsIntegerLiteral()
    {
        var script = new Script { Source = "x" }.WithResolvedValue(JsonNode.Parse("9007199254740993"));
        var result = ScriptResult.FromResolved(script);

        var json = result.ToJson();

        Assert.Equal("9007199254740993", json?.ToJsonString());
    }

    [Fact]
    public void To_Long_LargeIntegerViaResolvedNode_PreservesPrecision()
    {
        var script = new Script { Source = "x" }.WithResolvedValue(JsonNode.Parse("9007199254740993"));
        var result = ScriptResult.FromResolved(script);

        Assert.Equal(9007199254740993L, result.To<long>());
    }

    [Fact]
    public void ToClr_LargeIntegerViaScript_PreservesLong()
    {
        // 5e9 在 double 的精确整数范围内（<= 2^53），可经 Jint 无损失表达，验证脚本路径整数优先 long。
        var result = Evaluate("5000000000");

        var clr = result.ToClr();

        Assert.IsType<long>(clr);
        Assert.Equal(5000000000L, (long)clr!);
    }

    [Fact]
    public void ToJson_LargeIntegerViaScript_EmitsIntegerLiteral()
    {
        var result = Evaluate("5000000000");

        Assert.Equal("5000000000", result.ToJson()?.ToJsonString());
    }

    [Fact]
    public void ToClr_SmallInteger_StillInt()
    {
        var result = Evaluate("42");

        Assert.IsType<int>(result.ToClr());
        Assert.Equal(42, result.ToClr());
    }

    [Fact]
    public void ToClr_Double_StillDouble()
    {
        var result = Evaluate("3.14");

        Assert.IsType<double>(result.ToClr());
        Assert.Equal(3.14, result.ToClr());
    }

    [Fact]
    public void ToJson_Double_EmitsDecimalForm()
    {
        var result = Evaluate("3.14");

        Assert.Equal("3.14", result.ToJson()?.ToJsonString());
    }
}
