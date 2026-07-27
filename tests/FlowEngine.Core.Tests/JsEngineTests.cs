using System.Diagnostics;
using System.Text.Json.Nodes;
using FlowEngine.Core.Scripting;
using Jint;
using Jint.Native;

namespace FlowEngine.Core.Tests;

public class JsEngineTests
{
    [Fact]
    public void Create_DefaultOptions_ReturnsEngine()
    {
        var engine = JsEngine.Create();

        Assert.NotNull(engine);
    }

    [Fact]
    public void Evaluate_Expression_ReturnsValue()
    {
        using var engine = JsEngine.Create();

        var result = engine.Evaluate("1 + 1");

        Assert.Equal(2, result.AsNumber());
    }

    [Fact]
    public void Run_ScriptWithReturn_ReturnsValue()
    {
        using var engine = JsEngine.Create();

        var result = engine.Run("var x = 5; return x * 2;");

        Assert.Equal(10, result.AsNumber());
    }

    [Fact]
    public void SetValue_AndEvaluate_UsesValue()
    {
        using var engine = JsEngine.Create();
        engine.SetValue("x", 10);

        var result = engine.Evaluate("x * 2");

        Assert.Equal(20, result.AsNumber());
    }

    [Fact]
    public void ToDataItem_Null_ReturnsNullData()
    {
        using var engine = JsEngine.Create();

        var item = engine.ToDataItem(JsValue.Null);

        Assert.Null(item.Data);
        Assert.True(item.Success);
    }

    [Fact]
    public void ToDataItem_Boolean_ReturnsJsonValue()
    {
        using var engine = JsEngine.Create();

        var item = engine.ToDataItem(engine.Evaluate("true"));

        Assert.IsAssignableFrom<JsonValue>(item.Data);
        Assert.True(item.Data!.GetValue<bool>());
    }

    [Fact]
    public void ToDataItem_Number_ReturnsJsonValue()
    {
        using var engine = JsEngine.Create();

        var item = engine.ToDataItem(engine.Evaluate("42"));

        Assert.IsAssignableFrom<JsonValue>(item.Data);
        Assert.Equal(42.0, item.Data!.GetValue<double>());
    }

    [Fact]
    public void ToDataItem_String_ReturnsJsonValue()
    {
        using var engine = JsEngine.Create();

        var item = engine.ToDataItem(engine.Evaluate("'hello'"));

        Assert.IsAssignableFrom<JsonValue>(item.Data);
        Assert.Equal("hello", item.Data!.GetValue<string>());
    }

    [Fact]
    public void ToDataItem_Object_ReturnsJsonObject()
    {
        using var engine = JsEngine.Create();
        var result = engine.Run("return { a: 1 };");

        var item = engine.ToDataItem(result);

        Assert.IsAssignableFrom<JsonObject>(item.Data);
    }

    [Fact]
    public void PrepareExpression_ReturnsPreparedScript()
    {
        var prepared = JsEngine.PrepareExpression("1 + 1");

        var text = prepared.ToString();
        Assert.False(string.IsNullOrEmpty(text));
    }

    [Fact]
    public void EvaluatePrepared_ExecutesPreparedScript()
    {
        using var engine = JsEngine.Create();
        var prepared = JsEngine.PrepareExpression("1 + 1");

        var result = engine.EvaluatePrepared(prepared);

        Assert.Equal(2, result.AsNumber());
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var engine = JsEngine.Create();
        engine.Dispose();
        engine.Dispose();
    }

    [Fact]
    public async Task RunAsync_ShortExecutionTimeoutMs_NeverResolvingScript_TimeoutOccursQuickly()
    {
        // RED 先行：配置 100ms 超时，脚本 await 一个永不 resolve 的 Promise。
        // 永不 resolve 意味着引擎不会再"恢复执行语句"，因此引擎级 TimeoutInterval 不会触发，
        // 超时完全由 RunAsync 自己的 CancellationTokenSource 决定。
        // 修复前硬编码 5000ms，超时约 5s 后才触发（慢）；修复后应使用 100ms（快）。
        var options = new JsEngineOptions { ExecutionTimeoutMs = 100 };
        using var engine = JsEngine.Create(options);

        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await engine.RunAsync("await new Promise(() => {}); return 1;", TestContext.Current.CancellationToken));
        sw.Stop();

        // 修复前会等到 ~5s 才超时，远超 3s；修复后约 0.1s。用 3s 作为分界，避免误判。
        Assert.True(
            sw.Elapsed < TimeSpan.FromSeconds(3),
            $"RunAsync 未使用 ExecutionTimeoutMs：超时触发耗时 {sw.Elapsed}，预期远小于 3s");
    }

    [Fact]
    public async Task RunAsync_LargeExecutionTimeoutMs_FastScript_Completes()
    {
        // 反向验证：大超时 + 快速脚本应能正常返回，修复不得破坏正常路径。
        var options = new JsEngineOptions { ExecutionTimeoutMs = 5000 };
        using var engine = JsEngine.Create(options);
        engine.SetValue("sleep", new Func<Task>(() => Task.Delay(TimeSpan.FromMilliseconds(50))));

        var result = await engine.RunAsync("await sleep(); return 42;", TestContext.Current.CancellationToken);

        Assert.Equal(42, result.AsNumber());
    }

    [Fact]
    public async Task RunAsync_ZeroExecutionTimeoutMs_FallsBackToDefaultAndCompletes()
    {
        // 边界：ExecutionTimeoutMs 为 0/非法时回退到默认超时，快速脚本仍可完成。
        var options = new JsEngineOptions { ExecutionTimeoutMs = 0 };
        using var engine = JsEngine.Create(options);
        engine.SetValue("sleep", new Func<Task>(() => Task.Delay(TimeSpan.FromMilliseconds(50))));

        var result = await engine.RunAsync("await sleep(); return 7;", TestContext.Current.CancellationToken);

        Assert.Equal(7, result.AsNumber());
    }
}
