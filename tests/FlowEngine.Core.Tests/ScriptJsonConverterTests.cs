using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Tests;

public class ScriptJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = JsonDefaults.Options;

    [Fact]
    public void Read_Null_ReturnsNull()
    {
        var json = "null";
        var result = JsonSerializer.Deserialize<Script>(json, Options);

        Assert.Null(result);
    }

    [Fact]
    public void Read_String_ReturnsScriptWithSource()
    {
        var json = "\"x = 1\"";

        var result = JsonSerializer.Deserialize<Script>(json, Options);

        Assert.NotNull(result);
        Assert.Equal("x = 1", result!.Source);
    }

    [Fact]
    public void Read_True_ReturnsScriptWithTrueSource()
    {
        var json = "true";

        var result = JsonSerializer.Deserialize<Script>(json, Options);

        Assert.NotNull(result);
        Assert.Equal("true", result!.Source);
    }

    [Fact]
    public void Read_False_ReturnsScriptWithFalseSource()
    {
        var json = "false";

        var result = JsonSerializer.Deserialize<Script>(json, Options);

        Assert.NotNull(result);
        Assert.Equal("false", result!.Source);
    }

    [Fact]
    public void Read_Number_ReturnsScriptWithNumberSource()
    {
        var json = "42";

        var result = JsonSerializer.Deserialize<Script>(json, Options);

        Assert.NotNull(result);
        Assert.Equal("42", result!.Source);
    }

    [Fact]
    public void Read_Object_ReturnsScriptWithProperties()
    {
        var json = "{\"source\":\"x\",\"language\":\"JavaScript\",\"returnType\":\"String\"}";

        var result = JsonSerializer.Deserialize<Script>(json, Options);

        Assert.NotNull(result);
        Assert.Equal("x", result!.Source);
        Assert.Equal(ScriptReturnType.String, result.ReturnType);
    }

    [Fact]
    public void Read_Object_MissingOptional_ReturnsDefaults()
    {
        var json = "{\"source\":\"x\"}";

        var result = JsonSerializer.Deserialize<Script>(json, Options);

        Assert.NotNull(result);
        Assert.Equal(ScriptLanguage.JavaScript, result!.Language);
        Assert.Equal(ScriptReturnType.Object, result.ReturnType);
    }

    [Fact]
    public void Read_InvalidToken_ThrowsJsonException()
    {
        var json = "[]";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Script>(json, Options));
    }

    [Fact]
    public void Write_DefaultLanguageAndReturnType_WritesSourceOnly()
    {
        var script = new Script { Source = "x" };

        var json = JsonSerializer.Serialize(script, Options);

        Assert.Contains("\"source\":\"x\"", json);
        Assert.DoesNotContain("language", json);
        Assert.DoesNotContain("returnType", json);
    }

    [Fact]
    public void Write_NonDefaultLanguage_WritesLanguage()
    {
        var script = new Script { Source = "x", Language = ScriptLanguage.Python };

        var json = JsonSerializer.Serialize(script, Options);

        Assert.Contains("\"language\":\"Python\"", json);
    }

    [Fact]
    public void Write_NonDefaultReturnType_WritesReturnType()
    {
        var script = new Script { Source = "x", ReturnType = ScriptReturnType.String };

        var json = JsonSerializer.Serialize(script, Options);

        Assert.Contains("\"returnType\":\"String\"", json);
    }
}
