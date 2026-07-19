using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Data;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Tests;

public class ScriptingMoreTests
{
    [Fact]
    public void ScriptValueConverter_ToScript_FromScript_ReturnsSame()
    {
        var script = new Script { Source = "x" };

        var result = ScriptValueConverter.ToScript(script);

        Assert.Same(script, result);
    }

    [Fact]
    public void ScriptValueConverter_ToScript_FromString_CreatesScript()
    {
        var result = ScriptValueConverter.ToScript("x = 1");

        Assert.NotNull(result);
        Assert.Equal("x = 1", result!.Source);
        Assert.Equal(ScriptLanguage.JavaScript, result.Language);
        Assert.Equal(ScriptReturnType.String, result.ReturnType);
    }

    [Fact]
    public void ScriptValueConverter_ToScript_FromJsonElementString_CreatesScript()
    {
        var element = JsonDocument.Parse("\"x = 1\"").RootElement;

        var result = ScriptValueConverter.ToScript(element);

        Assert.NotNull(result);
        Assert.Equal("x = 1", result!.Source);
    }

    [Fact]
    public void ScriptValueConverter_ToScript_FromJsonElementObject_DeserializesScript()
    {
        var element = JsonDocument.Parse("{\"source\":\"x\",\"language\":\"JavaScript\",\"returnType\":\"String\"}").RootElement;

        var result = ScriptValueConverter.ToScript(element);

        Assert.NotNull(result);
        Assert.Equal("x", result!.Source);
    }

    [Fact]
    public void ScriptValueConverter_ToScript_FromInvalidJsonElement_ReturnsNull()
    {
        var element = JsonDocument.Parse("[]").RootElement;

        var result = ScriptValueConverter.ToScript(element);

        Assert.Null(result);
    }

    [Fact]
    public void ScriptValueConverter_ToScript_FromJsonNodeString_CreatesScript()
    {
        var node = JsonValue.Create("x = 1");

        var result = ScriptValueConverter.ToScript(node!);

        Assert.NotNull(result);
        Assert.Equal("x = 1", result!.Source);
    }

    [Fact]
    public void ScriptValueConverter_ToScript_FromJsonObject_DeserializesScript()
    {
        var node = JsonNode.Parse("{\"source\":\"x\",\"language\":\"JavaScript\",\"returnType\":\"String\"}");

        var result = ScriptValueConverter.ToScript(node!);

        Assert.NotNull(result);
        Assert.Equal("x", result!.Source);
    }

    [Fact]
    public void ScriptValueConverter_ToScript_FromUnsupportedType_ReturnsNull()
    {
        var result = ScriptValueConverter.ToScript(123);

        Assert.Null(result);
    }

    [Fact]
    public void ScriptValueConverter_TryGetScriptDictionary_FromDictionary_ReturnsTrue()
    {
        var dict = new Dictionary<string, Script> { ["a"] = new() { Source = "x" } };

        var success = ScriptValueConverter.TryGetScriptDictionary(dict, out var result);

        Assert.True(success);
        Assert.Same(dict, result);
    }

    [Fact]
    public void ScriptValueConverter_TryGetScriptDictionary_FromJsonElementObject_ReturnsTrue()
    {
        var element = JsonDocument.Parse("{\"a\":{\"source\":\"x\",\"language\":\"JavaScript\",\"returnType\":\"String\"}}").RootElement;

        var success = ScriptValueConverter.TryGetScriptDictionary(element, out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal("x", result!["a"].Source);
    }

    [Fact]
    public void ScriptValueConverter_TryGetScriptDictionary_FromJsonElementNonObject_ReturnsFalse()
    {
        var element = JsonDocument.Parse("\"not object\"").RootElement;

        var success = ScriptValueConverter.TryGetScriptDictionary(element, out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void ScriptValueConverter_TryGetScriptDictionary_FromJsonObject_ReturnsTrue()
    {
        var obj = JsonNode.Parse("{\"a\":{\"source\":\"x\",\"language\":\"JavaScript\",\"returnType\":\"String\"}}") as JsonObject;

        var success = ScriptValueConverter.TryGetScriptDictionary(obj!, out var result);

        Assert.True(success);
        Assert.NotNull(result);
    }

    [Fact]
    public void ScriptValueConverter_TryGetScriptDictionary_FromJsonNodeNonObject_ReturnsFalse()
    {
        var node = JsonValue.Create("not object");

        var success = ScriptValueConverter.TryGetScriptDictionary(node!, out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void ScriptValueConverter_TryGetScriptDictionary_FromString_ReturnsTrue()
    {
        var json = "{\"a\":{\"source\":\"x\",\"language\":\"JavaScript\",\"returnType\":\"String\"}}";

        var success = ScriptValueConverter.TryGetScriptDictionary(json, out var result);

        Assert.True(success);
        Assert.NotNull(result);
    }

    [Fact]
    public void ScriptValueConverter_TryGetScriptDictionary_FromInvalidString_ReturnsFalse()
    {
        var success = ScriptValueConverter.TryGetScriptDictionary("not json", out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void ScriptValueConverter_TryGetScriptDictionary_FromUnsupportedType_ReturnsFalse()
    {
        var success = ScriptValueConverter.TryGetScriptDictionary(123, out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void EnvironmentAccessor_WhitelistedVariable_ReturnsValue()
    {
        Environment.SetEnvironmentVariable("FLOW_TEST_VAR", "value");
        var accessor = new EnvironmentAccessor(new HashSet<string> { "FLOW_TEST_VAR" });

        var value = accessor["FLOW_TEST_VAR"];

        Assert.Equal("value", value);
        Environment.SetEnvironmentVariable("FLOW_TEST_VAR", null);
    }

    [Fact]
    public void EnvironmentAccessor_NonWhitelistedVariable_Throws()
    {
        var accessor = new EnvironmentAccessor(new HashSet<string> { "OTHER" });

        Assert.Throws<InvalidOperationException>(() => accessor["FLOW_TEST_VAR"]);
    }

    [Fact]
    public void EnvironmentAccessor_ToString_ReturnsNull()
    {
        var accessor = new EnvironmentAccessor(new HashSet<string>());

        Assert.Null(accessor.ToString());
    }

    [Fact]
    public void JsonValueConverter_Create_ReturnsConverter()
    {
        var converter = JsonValueConverter.Create(typeof(Dictionary<string, string>));

        Assert.NotNull(converter);
    }

    [Fact]
    public void JsonValueConverter_Generic_ConvertToProvider()
    {
        var converter = new JsonValueConverter<Dictionary<string, string>>(JsonDefaults.Options);
        var dict = new Dictionary<string, string> { ["key"] = "value" };

        var json = (string?)converter.ConvertToProvider.Invoke(dict);

        Assert.NotNull(json);
        Assert.Contains("key", json);
    }

    [Fact]
    public void JsonValueConverter_Generic_ConvertToProvider_Null_ReturnsNull()
    {
        var converter = new JsonValueConverter<Dictionary<string, string>?>(JsonDefaults.Options);

        var json = (string?)converter.ConvertToProvider.Invoke(null);

        Assert.Null(json);
    }

    [Fact]
    public void JsonValueConverter_Generic_ConvertFromProvider_Empty_ReturnsDefault()
    {
        var converter = new JsonValueConverter<Dictionary<string, string>>(JsonDefaults.Options);

        var result = (Dictionary<string, string>?)converter.ConvertFromProvider.Invoke(string.Empty);

        Assert.Null(result);
    }

    [Fact]
    public void JsonValueConverter_Generic_ConvertFromProvider_ValidJson_ReturnsObject()
    {
        var converter = new JsonValueConverter<Dictionary<string, string>>(JsonDefaults.Options);

        var result = (Dictionary<string, string>?)converter.ConvertFromProvider.Invoke("{\"key\":\"value\"}");

        Assert.NotNull(result);
        Assert.Equal("value", result!["key"]);
    }
}
