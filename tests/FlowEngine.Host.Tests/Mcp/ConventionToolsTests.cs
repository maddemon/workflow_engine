using System.Text.Json.Nodes;
using FlowEngine.Host.Mcp.Tools;

namespace FlowEngine.Host.Tests.Mcp;

public class ConventionToolsTests
{
    [Fact]
    public void GetConventions_StatesJavaScriptAndNoMustache()
    {
        var tools = new ConventionTools();
        var result = tools.GetConventions();

        Assert.Equal("javascript", result["expressionLanguage"]?.GetValue<string>());
        var summary = result["summary"]?.GetValue<string>() ?? "";
        Assert.Contains("JavaScript", summary);
        Assert.Contains("{{", summary);
        Assert.Contains("mustache", summary.ToLowerInvariant());

        var rules = result["rules"] as JsonArray;
        Assert.NotNull(rules);
        Assert.Contains(rules!, r => (r?.GetValue<string>() ?? "").Contains("{{"));
    }
}