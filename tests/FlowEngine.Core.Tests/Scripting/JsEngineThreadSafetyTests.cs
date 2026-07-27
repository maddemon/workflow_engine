using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Scripting;
using Jint;
using Jint.Native;
using Microsoft.Extensions.Options;

namespace FlowEngine.Core.Tests.Scripting;

/// <summary>
/// 验证 <see cref="JsEngine"/> 的线程安全串行化：同一实例并发调用被串行化，
/// 不同实例并发执行互不影响。
/// </summary>
public sealed class JsEngineThreadSafetyTests
{
    [Fact]
    public async Task EvaluatePrepared_SameInstance_ConcurrentCalls_AllCorrect()
    {
        using var engine = JsEngine.Create();
        var prepared = JsEngine.PrepareExpression("21 * 2"); // 期望 42

        const int threads = 24;
        const int iterationsPerThread = 100;

        var exceptions = new ConcurrentBag<Exception>();
        var wrongResults = new ConcurrentBag<double>();
        using var barrier = new Barrier(threads);

        var tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < iterationsPerThread; i++)
            {
                try
                {
                    var r = engine.EvaluatePrepared(prepared);
                    var value = r.AsNumber();
                    if (value != 42) wrongResults.Add(value);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
        Assert.Empty(wrongResults);
    }

    [Fact]
    public async Task Run_SameInstance_ConcurrentCalls_NoException()
    {
        using var engine = JsEngine.Create();

        const int threads = 24;
        const int iterationsPerThread = 100;

        var exceptions = new ConcurrentBag<Exception>();
        var wrongResults = new ConcurrentBag<double>();
        using var barrier = new Barrier(threads);

        var tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < iterationsPerThread; i++)
            {
                try
                {
                    var r = engine.Run("var x = 3; return x * 7;"); // 期望 21
                    var value = r.AsNumber();
                    if (value != 21) wrongResults.Add(value);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
        Assert.Empty(wrongResults);
    }

    [Fact]
    public async Task RunAsync_SameInstance_ConcurrentCalls_AllCorrect()
    {
        using var engine = JsEngine.Create();

        const int threads = 24;
        const int iterationsPerThread = 50;

        var exceptions = new ConcurrentBag<Exception>();
        var wrongResults = new ConcurrentBag<double>();
        using var barrier = new Barrier(threads);

        var tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < iterationsPerThread; i++)
            {
                try
                {
                    var r = await engine.RunAsync("return 11 * 2;", TestContext.Current.CancellationToken); // 期望 22
                    var value = r.AsNumber();
                    if (value != 22) wrongResults.Add(value);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
        Assert.Empty(wrongResults);
    }

    [Fact]
    public async Task DifferentInstances_Concurrent_ExecuteIndependently_AndInParallel()
    {
        const int instances = 12;

        var results = new ConcurrentBag<(int id, double value, Exception? ex)>();
        var concurrent = 0;
        var maxConcurrent = 0;
        using var barrier = new Barrier(instances);

        var tasks = Enumerable.Range(0, instances).Select(id => Task.Run(async () =>
        {
            // 每个实例拥有独立引擎与独立信号量，应可真正并行
            using var engine = JsEngine.Create();
            var expr = JsEngine.PrepareExpression($"{id} + 100");
            barrier.SignalAndWait();

            // 关键：在持有本实例 gate 的期间测量并发数。
            // 若存在全局静态锁，所有实例会在该静态锁上串行化，
            // 同一时刻只有 1 个实例能进入 gate → maxConcurrent == 1，测试失败；
            // 正确实现为每实例独立锁，各 gate 互不阻塞 → maxConcurrent 达到 instances。
            // 同时在本关键区内完成求值，确保"持锁并行"与"各自结果正确"一并验证。
            double value = 0;
            Exception? ex = null;
            using (await engine.LockAsync(TestContext.Current.CancellationToken))
            {
                try
                {
                    var cur = Interlocked.Increment(ref concurrent);
                    var observed = Volatile.Read(ref concurrent);
                    var prev = maxConcurrent;
                    while (observed > prev && Interlocked.CompareExchange(ref maxConcurrent, observed, prev) != prev)
                    {
                        prev = maxConcurrent;
                    }

                    Thread.SpinWait(50_000); // 制造可观测的重叠执行窗口

                    // 持锁期间求值（无锁内部入口），验证实例隔离与正确性
                    value = engine.EvaluatePreparedCore(expr).AsNumber();
                }
                catch (Exception e)
                {
                    ex = e;
                }
                finally
                {
                    Interlocked.Decrement(ref concurrent);
                }
            }

            results.Add((id, value, ex));
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.All(results, x => Assert.Null(x.ex));
        Assert.All(results, x => Assert.Equal(x.id + 100, x.value));
        // 证明不同实例在各自持有 gate 时仍并行（而非被某个全局锁串行化）
        Assert.True(maxConcurrent >= 2, $"期望并发数 >= 2，实际最大并发 = {maxConcurrent}");
    }

    [Fact]
    public async Task RunForItemAsync_SameSession_ConcurrentItems_NoScopeCrossTalk()
    {
        // 复现 OncePerItem 场景：同一引擎/会话被 Task.WhenAll 并发驱动多个 item。
        // 每个 item 注入各自的作用域（item.value / itemIndex），断言不发生作用域互相覆盖导致的错乱。
        var cache = new ScriptCache(Microsoft.Extensions.Options.Options.Create(new JsEngineOptions()));
        var script = new Script { Source = "$json.value + $itemIndex" };
        var prepared = cache.GetOrPrepare(script);

        var nodeContext = new NodeExecutionContext();
        var context = ScriptContext.From(nodeContext);

        const int items = 16;
        using var engine = JsEngine.Create();
        using var session = prepared.CreateSession(engine);

        var exceptions = new ConcurrentBag<Exception>();
        var wrong = new ConcurrentBag<(int index, object? actual)>();
        using var barrier = new Barrier(items);

        var tasks = Enumerable.Range(0, items).Select(index => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            var item = JsonNode.Parse($"{{\"value\":{index}}}")!;
            try
            {
                var result = await session.RunForItemAsync(prepared, context, item, index, TestContext.Current.CancellationToken);
                if (!result.Success || result.To<int>() != index + index)
                {
                    wrong.Add((index, result.Success ? result.To<object>() : result.Error?.Message));
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
        Assert.Empty(wrong);
    }
}
