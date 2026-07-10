using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Tests.Scripting.Models;

public sealed class ScriptTests
{
    [Fact]
    public void Default_Constructor_HasEmptySourceAndDefaults()
    {
        var script = new Script();

        Assert.Equal(string.Empty, script.Source);
        Assert.Equal(ScriptLanguage.JavaScript, script.Language);
        Assert.Equal(ScriptReturnType.Object, script.ReturnType);
        Assert.Null(script.ResolvedValue);
    }

    [Fact]
    public void Properties_AreInitOnly()
    {
        var script = new Script
        {
            Source = "1 + 1",
            Language = ScriptLanguage.JavaScript,
            ReturnType = ScriptReturnType.Number,
        };

        Assert.Equal("1 + 1", script.Source);
        Assert.Equal(ScriptReturnType.Number, script.ReturnType);
    }

    [Fact]
    public void WithResolvedValue_ReturnsNewInstance()
    {
        var original = new Script { Source = "1 + 1" };
        var value = JsonValue.Create(42);

        var updated = original.WithResolvedValue(value);

        Assert.NotSame(original, updated);
        Assert.Null(original.ResolvedValue);
        Assert.Same(value, updated.ResolvedValue);
        Assert.Equal(original.Source, updated.Source);
    }

    [Fact]
    public void GetResult_ReturnsTypedValue()
    {
        var script = new Script { Source = "x" }
            .WithResolvedValue(JsonValue.Create(42));

        Assert.Equal(42, script.GetResult<int>());
    }

    [Fact]
    public void GetResult_WithNullResolvedValue_ReturnsDefault()
    {
        var script = new Script { Source = "x" };

        Assert.Null(script.GetResult<int?>());
    }

    [Fact]
    public void Equals_IgnoresResolvedValue()
    {
        var a = new Script { Source = "1", ReturnType = ScriptReturnType.Number };
        var b = a.WithResolvedValue(JsonValue.Create(100));

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Equals_DifferentSources_AreNotEqual()
    {
        var a = new Script { Source = "1" };
        var b = new Script { Source = "2" };

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void GetHashCode_IgnoresResolvedValue()
    {
        var a = new Script { Source = "1" };
        var b = a.WithResolvedValue(JsonValue.Create(100));

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ImplicitConversion_FromString_CreatesScript()
    {
        Script script = "input.value";

        Assert.Equal("input.value", script.Source);
        Assert.Equal(ScriptLanguage.JavaScript, script.Language);
        Assert.Equal(ScriptReturnType.Object, script.ReturnType);
    }
}
