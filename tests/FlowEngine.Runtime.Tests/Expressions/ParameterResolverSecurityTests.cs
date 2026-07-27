using System.Text.Json.Nodes;
using FlowEngine.Core.Scripting;
using FlowEngine.Runtime.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlowEngine.Runtime.Tests.Expressions;

public class ParameterResolverSecurityTests
{
    private readonly ParameterResolver _resolver = new(
        NullLogger<ParameterResolver>.Instance,
        Options.Create(new JsEngineOptions()),
        new ScriptCache(Options.Create(new JsEngineOptions())));

    [Fact]
    public async Task Resolve_FetchIdentifier_Not_Recognized_As_Expression()
    {
        using var js = JsEngine.Create();
        js.SetValue("input", new JsonObject());
        var raw = new Dictionary<string, object> { ["url"] = "fetch" };

        var result = await _resolver.ResolveAsync(raw, js);

        // "fetch" should not be in knownIdentifiers, so it's treated as a plain string
        Assert.Equal("fetch", result["url"]);
    }

    [Fact]
    public async Task Resolve_ConsoleIdentifier_Not_Recognized_As_Expression()
    {
        using var js = JsEngine.Create();
        var raw = new Dictionary<string, object> { ["val"] = "console" };

        var result = await _resolver.ResolveAsync(raw, js);

        // "console" should not be in knownIdentifiers, so it's treated as a plain string
        Assert.Equal("console", result["val"]);
    }

    [Fact]
    public async Task Resolve_NowIdentifier_Recognized_As_Expression()
    {
        using var js = JsEngine.Create();
        js.SetValue("input", new JsonObject());
        var raw = new Dictionary<string, object> { ["ts"] = "now()" };

        var result = await _resolver.ResolveAsync(raw, js);

        // "now" should be in knownIdentifiers, so it's treated as an expression
        Assert.IsType<string>(result["ts"]);
        Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}", (string)result["ts"]);
    }

    [Fact]
    public async Task Resolve_Arithmetic_Expression_Still_Works()
    {
        using var js = JsEngine.Create();
        js.SetValue("input", new JsonObject { ["count"] = 5 });
        var raw = new Dictionary<string, object> { ["val"] = "input.count * 2" };

        var result = await _resolver.ResolveAsync(raw, js);

        Assert.Equal(10, result["val"]);
    }
}
