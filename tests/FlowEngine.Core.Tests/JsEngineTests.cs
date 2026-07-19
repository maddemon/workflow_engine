using System.Text.Json.Nodes;
using FlowEngine.Core.Scripting;
using Jint;
using Jint.Native;

namespace FlowEngine.Core.Tests;

public class JsEngineTests
{
    [Fact]
    public void Create_DefaultOptions_ReturnsEngine()
    {
        var engine = JsEngine.Create();

        Assert.NotNull(engine);
    }

    [Fact]
    public void Evaluate_Expression_ReturnsValue()
    {
        using var engine = JsEngine.Create();

        var result = engine.Evaluate("1 + 1");

        Assert.Equal(2, result.AsNumber());
    }

    [Fact]
    public void Run_ScriptWithReturn_ReturnsValue()
    {
        using var engine = JsEngine.Create();

        var result = engine.Run("var x = 5; return x * 2;");

        Assert.Equal(10, result.AsNumber());
    }

    [Fact]
    public void SetValue_AndEvaluate_UsesValue()
    {
        using var engine = JsEngine.Create();
        engine.SetValue("x", 10);

        var result = engine.Evaluate("x * 2");

        Assert.Equal(20, result.AsNumber());
    }

    [Fact]
    public void ToDataItem_Null_ReturnsNullData()
    {
        using var engine = JsEngine.Create();

        var item = engine.ToDataItem(JsValue.Null);

        Assert.Null(item.Data);
        Assert.True(item.Success);
    }

    [Fact]
    public void ToDataItem_Boolean_ReturnsJsonValue()
    {
        using var engine = JsEngine.Create();

        var item = engine.ToDataItem(engine.Evaluate("true"));

        Assert.IsAssignableFrom<JsonValue>(item.Data);
        Assert.True(item.Data!.GetValue<bool>());
    }

    [Fact]
    public void ToDataItem_Number_ReturnsJsonValue()
    {
        using var engine = JsEngine.Create();

        var item = engine.ToDataItem(engine.Evaluate("42"));

        Assert.IsAssignableFrom<JsonValue>(item.Data);
        Assert.Equal(42.0, item.Data!.GetValue<double>());
    }

    [Fact]
    public void ToDataItem_String_ReturnsJsonValue()
    {
        using var engine = JsEngine.Create();

        var item = engine.ToDataItem(engine.Evaluate("'hello'"));

        Assert.IsAssignableFrom<JsonValue>(item.Data);
        Assert.Equal("hello", item.Data!.GetValue<string>());
    }

    [Fact]
    public void ToDataItem_Object_ReturnsJsonObject()
    {
        using var engine = JsEngine.Create();
        var result = engine.Run("return { a: 1 };");

        var item = engine.ToDataItem(result);

        Assert.IsAssignableFrom<JsonObject>(item.Data);
    }

    [Fact]
    public void PrepareExpression_ReturnsPreparedScript()
    {
        var prepared = JsEngine.PrepareExpression("1 + 1");

        var text = prepared.ToString();
        Assert.False(string.IsNullOrEmpty(text));
    }

    [Fact]
    public void EvaluatePrepared_ExecutesPreparedScript()
    {
        using var engine = JsEngine.Create();
        var prepared = JsEngine.PrepareExpression("1 + 1");

        var result = engine.EvaluatePrepared(prepared);

        Assert.Equal(2, result.AsNumber());
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var engine = JsEngine.Create();
        engine.Dispose();
        engine.Dispose();
    }
}
