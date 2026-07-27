using System.Collections.Generic;
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

        using var engine = JsEngine.Create();
        using var session = prepared.CreateSession(engine);
        var result = await session.RunForItemAsync(prepared, context, item, 5, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(15, result.To<int>());
    }

    [Fact]
    public void Session_Dispose_DoesNotDisposeExternalEngine()
    {
        var script = new Script { Source = "1" };
        var prepared = new ScriptCache(Microsoft.Extensions.Options.Options.Create(new JsEngineOptions())).GetOrPrepare(script);
        using var engine = JsEngine.Create();
        using var session = prepared.CreateSession(engine);

        session.Dispose();

        // 默认情况下会话不拥有引擎，释放会话后引擎仍可用。
        engine.SetValue("x", 1);
    }

    [Fact]
    public void Session_Dispose_WhenOwnsEngine_DisposesEngine()
    {
        var engine = JsEngine.Create();
        using (var session = new PreparedScriptSession(engine, ownsEngine: true))
        {
        }

        Assert.Throws<ObjectDisposedException>(() => engine.SetValue("x", 1));
    }

    [Fact]
    public async Task Session_RunAsync_CompileError_ReturnsFailedResult()
    {
        var script = new Script { Source = "1 + +" };
        var prepared = new ScriptCache(Microsoft.Extensions.Options.Options.Create(new JsEngineOptions())).GetOrPrepare(script);

        using var engine = JsEngine.Create();
        using var session = prepared.CreateSession(engine);
        var result = await session.RunAsync(prepared, CreateContext(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Session_RunForItemAsync_CompileError_ReturnsFailedResult()
    {
        var script = new Script { Source = "1 + +" };
        var prepared = new ScriptCache(Microsoft.Extensions.Options.Options.Create(new JsEngineOptions())).GetOrPrepare(script);

        using var engine = JsEngine.Create();
        using var session = prepared.CreateSession(engine);
        var result = await session.RunForItemAsync(prepared, CreateContext(), null, 0, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Session_RunForItemAsync_DoesNotLeakGlobalsAcrossItems()
    {
        // Task 5-B：同一引擎跨 item 复用时，item1 经 globalThis 写入的全局不应在 item2 可见。
        // 默认沙箱移除 globalThis，故此处显式放行以构造真实泄漏场景（与运行时若放开 globalThis 一致）。
        var options = new JsEngineOptions();
        var allowed = new HashSet<string>(options.AllowedGlobals, StringComparer.OrdinalIgnoreCase);
        allowed.Add("globalThis");
        options.AllowedGlobals = allowed;
        var forbidden = new HashSet<string>(options.ForbiddenIdentifiers, StringComparer.OrdinalIgnoreCase);
        forbidden.Remove("globalThis");
        options.ForbiddenIdentifiers = forbidden;

        var cache = new ScriptCache(Microsoft.Extensions.Options.Options.Create(options));
        var leakScript = cache.GetOrPrepare(new Script { Source = "globalThis.__leak = 1" });
        var readScript = cache.GetOrPrepare(new Script { Source = "return typeof globalThis.__leak === 'undefined'" });

        var context = ScriptContext.From(new NodeExecutionContext());

        using var engine = JsEngine.Create(options);
        using var session = leakScript.CreateSession(engine);

        var item1 = await session.RunForItemAsync(leakScript, context, JsonNode.Parse("{}"), 0, TestContext.Current.CancellationToken);
        Assert.True(item1.Success, "item1 写入 globalThis.__leak 应成功");
        Assert.Equal(1, item1.To<int>());

        var item2 = await session.RunForItemAsync(readScript, context, JsonNode.Parse("{}"), 1, TestContext.Current.CancellationToken);
        Assert.True(item2.Success, "item2 读取 globalThis.__leak 应成功");
        // 修复后：item1 求值期间新增的全局已被清除，item2 不应再看到 item1 写入的 __leak。
        Assert.True(item2.To<bool>(), "跨 item 全局泄漏防护：item2 不应看到 item1 写入的 globalThis.__leak");
    }
}
