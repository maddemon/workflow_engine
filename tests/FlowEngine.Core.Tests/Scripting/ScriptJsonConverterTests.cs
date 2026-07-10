using System.Text.Json;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Core.Scripting.Models;

namespace FlowEngine.Core.Tests.Scripting;

public sealed class ScriptJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = JsonDefaults.Options;

    [Fact]
    public void Serialize_DefaultValues_EmitsOnlySource()
    {
        var script = new Script { Source = "1 + 1" };

        var json = JsonSerializer.Serialize(script, Options);

        Assert.Equal("""{"source":"1 + 1"}""", json);
    }

    [Fact]
    public void Serialize_NonDefaultLanguage_EmitsLanguage()
    {
        var script = new Script { Source = "print('hi')", Language = ScriptLanguage.Python };

        var json = JsonSerializer.Serialize(script, Options);

        Assert.Equal("""{"source":"print('hi')","language":"Python"}""", json);
    }

    [Fact]
    public void Serialize_NonDefaultReturnType_EmitsReturnType()
    {
        var script = new Script { Source = "true", ReturnType = ScriptReturnType.Bool };

        var json = JsonSerializer.Serialize(script, Options);

        Assert.Equal("""{"source":"true","returnType":"Bool"}""", json);
    }

    [Fact]
    public void Deserialize_StringShorthand_Works()
    {
        var script = JsonSerializer.Deserialize<Script>("\"input.value\"", Options);

        Assert.NotNull(script);
        Assert.Equal("input.value", script.Source);
        Assert.Equal(ScriptLanguage.JavaScript, script.Language);
        Assert.Equal(ScriptReturnType.Object, script.ReturnType);
    }

    [Fact]
    public void Deserialize_ObjectForm_Works()
    {
        var script = JsonSerializer.Deserialize<Script>("""{"source":"x","language":"JavaScript","returnType":"Number"}""", Options);

        Assert.NotNull(script);
        Assert.Equal("x", script.Source);
        Assert.Equal(ScriptLanguage.JavaScript, script.Language);
        Assert.Equal(ScriptReturnType.Number, script.ReturnType);
    }

    [Fact]
    public void Deserialize_ObjectForm_MissingOptionalDefaults()
    {
        var script = JsonSerializer.Deserialize<Script>("""{"source":"x"}""", Options);

        Assert.NotNull(script);
        Assert.Equal(ScriptLanguage.JavaScript, script.Language);
        Assert.Equal(ScriptReturnType.Object, script.ReturnType);
    }

    [Fact]
    public void Deserialize_Null_ReturnsNull()
    {
        var script = JsonSerializer.Deserialize<Script>("null", Options);

        Assert.Null(script);
    }

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var original = new Script { Source = "x", Language = ScriptLanguage.Python, ReturnType = ScriptReturnType.Dictionary };

        var json = JsonSerializer.Serialize(original, Options);
        var roundTripped = JsonSerializer.Deserialize<Script>(json, Options);

        Assert.Equal(original, roundTripped);
    }
}
