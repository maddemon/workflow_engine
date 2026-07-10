using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Tests.Scripting;

public sealed class PreparedScriptTests
{
    private static ScriptContext CreateContext(Dictionary<string, object?>? globals = null)
    {
        var nodeContext = new NodeExecutionContext
        {
            GlobalVariables = globals,
        };
        return ScriptContext.From(nodeContext);
    }

    [Fact]
    public async Task RunAsync_SingleExpression_ReturnsValue()
    {
        var cache = new ScriptCache(Microsoft.Extensions.Options.Options.Create(new JsEngineOptions()));
        var script = new Script { Source = "1 + 2" };
        var prepared = cache.GetOrPrepare(script);

        var result = await prepared.RunAsync(CreateContext(), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(3, result.To<int>());
    }

    [Fact]
    public async Task RunAsync_WithExtraGlobals_UsesGlobals()
    {
        var cache = new ScriptCache(Microsoft.Extensions.Options.Options.Create(new JsEngineOptions()));
        var script = new Script { Source = "x * 2" };
        var prepared = cache.GetOrPrepare(script);
        var context = new ScriptContext(
            new NodeExecutionContext(),
            new Dictionary<string, object?> { ["x"] = 21 });

        var result = await prepared.RunAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(42, result.To<int>());
    }

    [Fact]
    public async Task RunAsync_SyntaxError_ReturnsFailedResult()
    {
        var cache = new ScriptCache(Microsoft.Extensions.Options.Options.Create(new JsEngineOptions()));
        var script = new Script { Source = "1 + +" };
        var prepared = cache.GetOrPrepare(script);

        var result = await prepared.RunAsync(CreateContext(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task RunAsync_WithReusedEngine_Works()
    {
        var cache = new ScriptCache(Microsoft.Extensions.Options.Options.Create(new JsEngineOptions()));
        var script = new Script { Source = "value + 1" };
        var prepared = cache.GetOrPrepare(script);

        using var engine = JsEngine.Create();
        engine.SetValue("value", 10);
        var result = await prepared.RunAsync(CreateContext(), engine, TestContext.Current.CancellationToken);

        Assert.Equal(11, result.To<int>());
    }

    [Fact]
    public async Task Session_RunForItemAsync_ProvidesItemScope()
    {
        var cache = new ScriptCache(Microsoft.Extensions.Options.Options.Create(new JsEngineOptions()));
        var script = new Script { Source = "$json.value + $itemIndex" };
        var prepared = cache.GetOrPrepare(script);

        var nodeContext = new NodeExecutionContext();
        var context = ScriptContext.From(nodeContext);
        var item = JsonNode.Parse("""{"value":10}""")!;

        using var session = prepared.CreateSession(JsEngine.Create());
        var result = await session.RunForItemAsync(prepared, context, item, 5, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(15, result.To<int>());
    }

    [Fact]
    public async Task Session_Dispose_DisposesEngine()
    {
        var script = new Script { Source = "1" };
        var prepared = new ScriptCache(Microsoft.Extensions.Options.Options.Create(new JsEngineOptions())).GetOrPrepare(script);
        var engine = JsEngine.Create();
        var session = prepared.CreateSession(engine);

        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => engine.SetValue("x", 1));
    }

    [Fact]
    public async Task Session_RunAsync_CompileError_ReturnsFailedResult()
    {
        var script = new Script { Source = "1 + +" };
        var prepared = new ScriptCache(Microsoft.Extensions.Options.Options.Create(new JsEngineOptions())).GetOrPrepare(script);

        using var session = prepared.CreateSession(JsEngine.Create());
        var result = await session.RunAsync(prepared, CreateContext(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Session_RunForItemAsync_CompileError_ReturnsFailedResult()
    {
        var script = new Script { Source = "1 + +" };
        var prepared = new ScriptCache(Microsoft.Extensions.Options.Options.Create(new JsEngineOptions())).GetOrPrepare(script);

        using var session = prepared.CreateSession(JsEngine.Create());
        var result = await session.RunForItemAsync(prepared, CreateContext(), null, 0, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }
}
