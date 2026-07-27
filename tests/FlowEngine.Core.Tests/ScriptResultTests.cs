using System.Text.Json.Nodes;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;
using Jint;

namespace FlowEngine.Core.Tests;

public class ScriptResultTests
{
    [Fact]
    public void ScriptResult_FromResolved_NullResolvedValue_Success()
    {
        var script = new Script { Source = "x" };

        var result = ScriptResult.FromResolved(script);

        Assert.True(result.Success);
        Assert.Same(script, result.Original);
    }

    [Fact]
    public void ScriptResult_FromResolved_WithResolvedValue_ToClr_ReturnsValue()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create(42) };

        var result = ScriptResult.FromResolved(script);

        // 普通整数应保留 int 类型（而非被统一装箱为 double）。
        Assert.IsType<int>(result.ToClr());
        Assert.Equal(42, result.ToClr());
    }

    [Fact]
    public void ScriptResult_FromResolved_StringValue_ToClr_ReturnsString()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create("hello") };

        var result = ScriptResult.FromResolved(script);

        Assert.Equal("hello", result.ToClr());
    }

    [Fact]
    public void ScriptResult_FromResolved_BooleanValue_ToBoolean_ReturnsBool()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create(true) };

        var result = ScriptResult.FromResolved(script);

        Assert.True(result.ToBoolean());
    }

    [Fact]
    public void ScriptResult_FromResolved_NullValue_ToBoolean_ReturnsFalse()
    {
        var script = new Script { Source = "x", ResolvedValue = null };

        var result = ScriptResult.FromResolved(script);

        Assert.False(result.ToBoolean());
    }

    [Fact]
    public void ScriptResult_FromResolved_NumberValue_ToJson_ReturnsJsonValue()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create(42) };

        var result = ScriptResult.FromResolved(script);

        var json = result.ToJson();
        Assert.IsAssignableFrom<JsonValue>(json);
        Assert.Equal(42, json!.GetValue<int>());
    }

    [Fact]
    public void ScriptResult_FromResolved_StringValue_ToJson_ReturnsJsonValue()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create("hello") };

        var result = ScriptResult.FromResolved(script);

        var json = result.ToJson();
        Assert.IsAssignableFrom<JsonValue>(json);
        Assert.Equal("hello", json!.GetValue<string>());
    }

    [Fact]
    public void ScriptResult_FromResolved_ToString_ReturnsString()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create(42) };

        var result = ScriptResult.FromResolved(script);

        Assert.Equal("42", result.To<string>());
    }

    [Fact]
    public void ScriptResult_FromResolved_ToBool_ReturnsBool()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create(true) };

        var result = ScriptResult.FromResolved(script);

        Assert.True(result.To<bool>());
    }

    [Fact]
    public void ScriptResult_FromResolved_ToInt_ReturnsInt()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create(42) };

        var result = ScriptResult.FromResolved(script);

        Assert.Equal(42, result.To<int>());
    }

    [Fact]
    public void ScriptResult_FromResolved_ToLong_ReturnsLong()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create(42) };

        var result = ScriptResult.FromResolved(script);

        Assert.Equal(42L, result.To<long>());
    }

    [Fact]
    public void ScriptResult_FromResolved_ToDouble_ReturnsDouble()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create(3.14) };

        var result = ScriptResult.FromResolved(script);

        Assert.Equal(3.14, result.To<double>());
    }

    [Fact]
    public void ScriptResult_FromResolved_ToJsonNode_ReturnsNode()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create("hello") };

        var result = ScriptResult.FromResolved(script);

        var node = result.To<JsonNode>();
        Assert.NotNull(node);
        Assert.Equal("hello", node.GetValue<string>());
    }

    [Fact]
    public void ScriptResult_Failed_ToClr_Throws()
    {
        var script = new Script { Source = "x" };
        var error = new ScriptErrorException(script, "err");
        var result = new ScriptResult(script, error);

        Assert.Throws<ScriptErrorException>(() => result.ToClr());
    }

    [Fact]
    public void ScriptResult_Failed_ToBoolean_Throws()
    {
        var script = new Script { Source = "x" };
        var error = new ScriptErrorException(script, "err");
        var result = new ScriptResult(script, error);

        Assert.Throws<ScriptErrorException>(() => result.ToBoolean());
    }

    [Fact]
    public void ScriptResult_Failed_ToJson_Throws()
    {
        var script = new Script { Source = "x" };
        var error = new ScriptErrorException(script, "err");
        var result = new ScriptResult(script, error);

        Assert.Throws<ScriptErrorException>(() => result.ToJson());
    }

    [Fact]
    public void ScriptResult_Failed_ToGeneric_Throws()
    {
        var script = new Script { Source = "x" };
        var error = new ScriptErrorException(script, "err");
        var result = new ScriptResult(script, error);

        Assert.Throws<ScriptErrorException>(() => result.To<string>());
    }

    [Fact]
    public void ScriptResult_Constructor_NullOriginal_Throws()
    {
        var engine = new Engine();
        Assert.Throws<ArgumentNullException>(() => new ScriptResult((Script)null!, engine.Evaluate("1")));
    }

    [Fact]
    public void ScriptResult_Constructor_NullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ScriptResult(new Script { Source = "x" }, (ScriptErrorException)null!));
    }
}
