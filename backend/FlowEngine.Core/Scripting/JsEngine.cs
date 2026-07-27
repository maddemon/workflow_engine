using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.Scripting;

using JintPreparedScript = Jint.Prepared<Acornima.Ast.Script>;

/// <summary>
/// Jint JavaScript 引擎封装。
/// 提供安全的表达式求值（参数、条件）和脚本执行（JSNode/CodeSnippet），
/// 内置 console polyfill 和安全白名单函数，禁止网络/文件系统/进程/反射访问。
/// </summary>
public sealed class JsEngine : IDisposable
{
    private readonly Engine _engine;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    private JsEngine(Engine engine, ILogger? logger)
    {
        _engine = engine;
        _logger = logger;
    }

    /// <summary>
    /// 创建 JsEngine 实例。每个实例有独立的沙箱。
    /// </summary>
    /// <param name="options">JS 引擎安全限制配置。为 null 时使用默认值。</param>
    /// <param name="configure">可选的 Engine 选项配置回调。</param>
    /// <param name="logger">可选的日志记录器。</param>
    public static JsEngine Create(JsEngineOptions? options = null, Action<Options>? configure = null, ILogger? logger = null)
    {
        var opts = options ?? new JsEngineOptions();
        var engine = new Engine(o =>
        {
            o.Strict = true;
            o.TimeoutInterval(TimeSpan.FromMilliseconds(opts.ExecutionTimeoutMs));
            o.LimitMemory(opts.MemoryLimitBytes);
            o.MaxStatements(opts.MaxStatements);
            o.LimitRecursion(opts.RecursionDepthLimit);
            o.RegexTimeoutInterval(TimeSpan.FromMilliseconds(opts.RegexTimeoutMs));
            o.MaxArraySize((uint)opts.ArraySizeLimit);
            o.DisableStringCompilation();
            // M1：默认不调用 AllowClr()，脚本无法访问 CLR 类型/对象，从而封死借 constructor 逃逸执行 .NET 代码。
            configure?.Invoke(o);
        });

        // SEC-7：在注入任何辅助函数之前，先按白名单裁剪全局对象，从源头移除危险标识符
        // （process/require/globalThis 等），即使借字符串拼接绕过黑名单也无法访问 CLR（未启用 AllowClr）。
        ApplySandboxWhitelist(engine, opts);

        InjectConsole(engine, logger);
        InjectNow(engine);
        InjectJmespath(engine, logger);
        InjectWhitelistFunctions(engine);

        return new JsEngine(engine, logger);
    }

    /// <summary>
    /// 沙箱白名单裁剪（SEC-7）：删除全局对象上所有<b>不在</b> <see cref="JsEngineOptions.AllowedGlobals"/>
    /// 内的自有属性，并把 <see cref="JsEngineOptions.ForbiddenIdentifiers"/> 中的标识符再次显式删除（纵深防御）。
    /// 黑名单仅作兜底，白名单才是防逃逸主防线。
    /// </summary>
    private static void ApplySandboxWhitelist(Engine engine, JsEngineOptions opts)
    {
        var allowed = opts.AllowedGlobals;
        var forbidden = opts.ForbiddenIdentifiers;

        var toDelete = new List<JsValue>();
        foreach (var property in engine.Global.GetOwnProperties())
        {
            var name = property.Key.ToString();
            if (!allowed.Contains(name) || forbidden.Contains(name))
            {
                toDelete.Add(property.Key);
            }
        }

        foreach (var name in toDelete)
        {
            engine.Global.Delete(name);
        }

        // 纵深防御：确保黑名单标识符（即便经由原型链或其他方式暴露）均被移除。
        foreach (var bad in forbidden)
        {
            engine.Global.Delete(JsValue.FromObject(engine, bad));
        }

        // SEC-7 关键加固：白名单仅删除全局对象的"自有"属性，而 <c>this['cons'+'tructor']</c>
        // 之类逃逸经由<b>原型链</b>访问 <c>constructor</c>，从而拿到 CLR 类型并借 <c>.System</c> 泄露。
        // 因此必须同时从全局对象的原型（即 Object.prototype）上移除 <c>constructor</c>，
        // 彻底切断这条逃逸路径（沙箱内脚本无需依赖 constructor 属性）。
        var proto = engine.Global.Prototype;
        while (proto is not null && proto != JsValue.Null)
        {
            if (proto is JsObject protoObj)
            {
                protoObj.Delete("constructor");
            }

            if (ReferenceEquals(proto, proto.Prototype))
            {
                break;
            }

            proto = proto.Prototype;
        }
    }

    /// <summary>
    /// 设置 JS 全局变量（如 input, nodes, workflow 等上下文）。
    /// </summary>
    public void SetValue(string name, object? value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _engine.SetValue(name, value);
    }

    /// <summary>
    /// 执行纯表达式求值（无 return 语句，自动包装）。
    /// 示例：Evaluate("input.status === 'active'") → true
    /// </summary>
    public JsValue Evaluate(string expression)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gate.Wait();
        try
        {
            return _engine.Evaluate($"return ({expression})");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 执行完整 JS 脚本（需含 return 语句，自动包装 IIFE）。
    /// 示例：Run("const x = input.count * 2; return x;")
    /// </summary>
    public JsValue Run(string script)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gate.Wait();
        try
        {
            return _engine.Evaluate($"(function() {{ {script} }})()");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 异步执行脚本，支持 await。
    /// </summary>
    public async Task<JsValue> RunAsync(string script, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 异步获取实例锁，避免阻塞线程池线程；取消信号量等待时一并尊重外部取消。
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 使用 CancellationTokenSource 强制超时，防止异步操作长时间挂起
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(5000)); // 默认 5 秒超时

            try
            {
                var result = _engine.Evaluate($"(async function() {{ {script} }})()");
                return await result.UnwrapIfPromiseAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("脚本执行超时");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 预编译表达式，返回可缓存的 AST。
    /// </summary>
    public static JintPreparedScript PrepareExpression(string expression)
    {
        return Engine.PrepareScript($"return ({expression})", expression, strict: true);
    }

    /// <summary>
    /// 执行已预编译的表达式 AST。
    /// </summary>
    public JsValue EvaluatePrepared(JintPreparedScript prepared)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gate.Wait();
        try
        {
            return EvaluatePreparedCore(prepared);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 无锁执行已预编译表达式（内部使用）。调用方须保证已持有实例锁。
    /// 用于 <see cref="LockAsync"/> 包裹的复合关键区（如作用域注入 + 求值）。
    /// </summary>
    internal JsValue EvaluatePreparedCore(JintPreparedScript prepared)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _engine.Evaluate(in prepared);
    }

    /// <summary>
    /// 进入实例级串行锁。把多次引擎访问（如作用域注入 + 求值）作为原子操作，
    /// 避免同一 <see cref="JsEngine"/> 实例被并发驱动时结果错乱。
    /// 锁作用域仅限本实例，不影响其他实例的并行。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async ValueTask<IDisposable> LockAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new GateReleaser(_gate);
    }

    /// <summary>
    /// 实例锁释放器。
    /// </summary>
    private sealed class GateReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _released;

        public GateReleaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            if (!_released)
            {
                _released = true;
                _semaphore.Release();
            }
        }
    }

    /// <summary>
    /// JsValue → DataItem 转换（用于 JSNode/CodeSnippet 输出）。
    /// </summary>
    public DataItem ToDataItem(JsValue result)
    {
        if (result.IsUndefined() || result.IsNull())
        {
            return new DataItem { Data = null, Success = true, SourceIndex = 0 };
        }

        if (result.IsBoolean())
        {
            return new DataItem { Data = JsonValue.Create(result.AsBoolean()), Success = true, SourceIndex = 0 };
        }

        if (result.IsNumber())
        {
            return new DataItem { Data = JsonValue.Create(result.AsNumber()), Success = true, SourceIndex = 0 };
        }

        if (result.IsString())
        {
            return new DataItem { Data = JsonValue.Create(result.AsString()), Success = true, SourceIndex = 0 };
        }

        if (result.IsObject() || result.IsArray())
        {
            try
            {
                var json = JsonSerializer.SerializeToNode(result.ToObject());
                return new DataItem { Data = json, Success = true, SourceIndex = 0 };
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "JsValue 序列化为 JSON 失败，将尝试 ToString 回退。");
            }
        }

        var str = result.ToString();
        try
        {
            return new DataItem { Data = JsonNode.Parse(str), Success = true, SourceIndex = 0 };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "JsValue.ToString() 解析为 JSON 失败，将作为字符串返回。");
            return new DataItem { Data = JsonValue.Create(str), Success = true, SourceIndex = 0 };
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _engine.Dispose();
            _gate.Dispose();
        }
    }

    /// <summary>
    /// ES 日期 -> yyyy-MM-dd HH:mm:ss 格式。
    /// </summary>
    private static string FormatDateTime(DateTime dt) => dt.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>
    /// ES 日期 -> ISO 8601 格式。
    /// </summary>
    private static string FormatIsoDateTime(DateTime dt) => dt.ToString("o");

    private static void InjectConsole(Engine engine, ILogger? logger)
    {
        engine.SetValue("console", new
        {
            log = new Action<object?>(msg =>
            {
                logger?.LogInformation("[JS] {Msg}", msg?.ToString() ?? "");
            }),
            info = new Action<object?>(msg =>
            {
                logger?.LogInformation("[JS] {Msg}", msg?.ToString() ?? "");
            }),
            warn = new Action<object?>(msg =>
            {
                logger?.LogWarning("[JS] {Msg}", msg?.ToString() ?? "");
            }),
            error = new Action<object?>(msg =>
            {
                logger?.LogError("[JS] {Msg}", msg?.ToString() ?? "");
            }),
        });
    }

    private static void InjectNow(Engine engine)
    {
        engine.SetValue("now", new Func<string?>(() => FormatDateTime(DateTime.UtcNow)));
        engine.SetValue("nowIso", new Func<string>(() => FormatIsoDateTime(DateTime.UtcNow)));
    }

    private static void InjectJmespath(Engine engine, ILogger? logger)
    {
        engine.SetValue("jmespath", new Func<JsonNode?, string, object?>((data, query) =>
        {
            if (data is null) return null;

            // 简单路径查询: "foo.bar[0].baz"
            // 完整 JMESPath 需引入第三方库，当前实现基础路径导航
            // 统一返回 JSON 字符串，避免标量与对象返回类型不一致
            try
            {
                var result = JsonPath.GetNode(data, query);
                return result?.ToJsonString();
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "jmespath 查询失败：{Query}", query);
                return null;
            }
        }));
    }

    private static void InjectWhitelistFunctions(Engine engine)
    {
        engine.SetValue("length", new Func<object?, int?>(value =>
        {
            return value switch
            {
                string s => s.Length,
                JsonNode node when node is JsonArray arr => arr.Count,
                JsonNode node when node is JsonObject obj => obj.Count,
                JsonElement element when element.ValueKind == JsonValueKind.Array => element.GetArrayLength(),
                JsonElement element when element.ValueKind == JsonValueKind.Object => element.EnumerateObject().Count(),
                IEnumerable<object> enumerable => enumerable.Count(),
                _ => null
            };
        }));

        engine.SetValue("trim", new Func<string?, string?>(value => value?.Trim()));
    }

}
