using System.Text.Json.Nodes;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Tests;

public class ScriptTests
{
    [Fact]
    public void Script_ImplicitOperator_FromString_CreatesScript()
    {
        Script script = "x = 1";

        Assert.Equal("x = 1", script.Source);
        Assert.Equal(ScriptLanguage.JavaScript, script.Language);
        Assert.Equal(ScriptReturnType.Object, script.ReturnType);
    }

    [Fact]
    public void Script_WithResolvedValue_ReturnsNewInstance()
    {
        var script = new Script { Source = "x" };
        var value = JsonValue.Create(42);

        var result = script.WithResolvedValue(value);

        Assert.NotSame(script, result);
        Assert.Equal(script.Source, result.Source);
        Assert.Same(value, result.ResolvedValue);
    }

    [Fact]
    public void Script_GetResult_NullResolvedValue_ReturnsDefault()
    {
        var script = new Script { Source = "x" };

        var result = script.GetResult<string>();

        Assert.Null(result);
    }

    [Fact]
    public void Script_GetResult_StringFromStringValue_ReturnsString()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create("hello") };

        var result = script.GetResult<string>();

        Assert.Equal("hello", result);
    }

    [Fact]
    public void Script_GetResult_StringFromIntValue_CoercesToString()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create(42) };

        var result = script.GetResult<string>();

        Assert.Equal("42", result);
    }

    [Fact]
    public void Script_GetResult_StringFromBoolValue_CoercesToString()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create(true) };

        var result = script.GetResult<string>();

        Assert.Equal("True", result);
    }

    [Fact]
    public void Script_GetResult_StringFromDoubleValue_CoercesToString()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create(3.14) };

        var result = script.GetResult<string>();

        Assert.Equal("3.14", result);
    }

    [Fact]
    public void Script_GetResult_StringFromLongValue_CoercesToString()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create(42L) };

        var result = script.GetResult<string>();

        Assert.Equal("42", result);
    }

    [Fact]
    public void Script_GetResult_StringFromObjectValue_CoercesToJsonString()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonNode.Parse("{\"a\":1}") };

        var result = script.GetResult<string>();

        Assert.Contains("\"a\":1", result);
    }

    [Fact]
    public void Script_GetResult_IntValue_ReturnsInt()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create(42) };

        var result = script.GetResult<int>();

        Assert.Equal(42, result);
    }

    [Fact]
    public void Script_GetResult_ObjectDeserialization_ReturnsObject()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonNode.Parse("{\"a\":1}") };

        var result = script.GetResult<Dictionary<string, int>>();

        Assert.NotNull(result);
        Assert.Equal(1, result!["a"]);
    }

    [Fact]
    public void Script_Equality_SameValues_AreEqual()
    {
        var a = new Script { Source = "x", Language = ScriptLanguage.JavaScript, ReturnType = ScriptReturnType.String };
        var b = new Script { Source = "x", Language = ScriptLanguage.JavaScript, ReturnType = ScriptReturnType.String };

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Script_Equality_DifferentValues_AreNotEqual()
    {
        var a = new Script { Source = "x" };
        var b = new Script { Source = "y" };

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void Script_Equality_Null_IsNotEqual()
    {
        var a = new Script { Source = "x" };

        Assert.False(a == null);
        Assert.True(a != null);
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void Script_GetHashCode_SameValues_AreEqual()
    {
        var a = new Script { Source = "x", Language = ScriptLanguage.JavaScript, ReturnType = ScriptReturnType.String };
        var b = new Script { Source = "x", Language = ScriptLanguage.JavaScript, ReturnType = ScriptReturnType.String };

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Script_Empty_ReturnsEmptyScript()
    {
        var empty = Script.Empty;

        Assert.NotNull(empty);
        Assert.Empty(empty.Source);
    }

    [Fact]
    public void Script_Equals_Object_CallsTypedEquals()
    {
        var a = new Script { Source = "x" };
        var b = new Script { Source = "x" };

        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals("not a script"));
    }

    [Fact]
    public void Script_GetResult_StringFromUnsupportedJsonValue_FallsThroughToJsonString()
    {
        var script = new Script { Source = "x", ResolvedValue = JsonValue.Create(Guid.NewGuid()) };

        var result = script.GetResult<string>();

        Assert.NotNull(result);
    }
}
