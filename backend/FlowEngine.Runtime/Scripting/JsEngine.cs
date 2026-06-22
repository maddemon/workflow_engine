using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Scripting;

/// <summary>
/// Jint JavaScript 引擎封装。
/// 提供安全的表达式求值（参数、条件）和脚本执行（JSNode/CodeSnippet），
/// 内置 console polyfill 和安全白名单函数，禁止网络/文件系统/进程/反射访问。
/// </summary>
public sealed class JsEngine : IDisposable
{
    private readonly Engine _engine;
    private readonly ILogger? _logger;
    private bool _disposed;

    private JsEngine(Engine engine, ILogger? logger)
    {
        _engine = engine;
        _logger = logger;
    }

    /// <summary>
    /// 创建 JsEngine 实例。每个实例有独立的沙箱。
    /// </summary>
    /// <param name="configure">可选的 Engine 选项配置回调。</param>
    /// <param name="logger">可选的日志记录器。</param>
    public static JsEngine Create(Action<Options>? configure = null, ILogger? logger = null)
    {
        var engine = new Engine(options =>
        {
            options.Strict = true;
            options.TimeoutInterval(TimeSpan.FromSeconds(5));
            options.LimitMemory(8_000_000);
            options.MaxStatements(5000);
            options.LimitRecursion(50);
            options.RegexTimeoutInterval(TimeSpan.FromSeconds(2));
            options.MaxArraySize(100_000);
            options.DisableStringCompilation();
            configure?.Invoke(options);
        });

        InjectConsole(engine, logger);
        InjectNow(engine);
        InjectJmespath(engine);

        return new JsEngine(engine, logger);
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
        return _engine.Evaluate($"return ({expression})");
    }

    /// <summary>
    /// 执行完整 JS 脚本（需含 return 语句，自动包装 IIFE）。
    /// 示例：Run("const x = input.count * 2; return x;")
    /// </summary>
    public JsValue Run(string script)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _engine.Evaluate($"(function() {{ {script} }})()");
    }

    /// <summary>
    /// 异步执行脚本，支持 await。
    /// </summary>
    public async Task<JsValue> RunAsync(string script, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = _engine.Evaluate($"(async function() {{ {script} }})()");
        return await result.UnwrapIfPromiseAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// JsValue → System.Text.Json 兼容类型转换（用于参数求值结果）。
    /// </summary>
    public static object? ToClrValue(JsValue value)
    {
        if (value.IsUndefined() || value.IsNull())
        {
            return null;
        }

        if (value.IsBoolean())
        {
            return value.AsBoolean();
        }

        if (value.IsNumber())
        {
            var num = value.AsNumber();
            return num == Math.Floor(num) && num is >= int.MinValue and <= int.MaxValue
                ? (int)num
                : num;
        }

        if (value.IsString())
        {
            return value.AsString();
        }

        return JsonNode.Parse(value.ToString());
    }

    /// <summary>
    /// JsValue → DataItem 转换（用于 JSNode/CodeSnippet 输出）。
    /// </summary>
    public static DataItem ToDataItem(JsValue result)
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
            catch
            {
            }
        }

        var str = result.ToString();
        try
        {
            return new DataItem { Data = JsonNode.Parse(str), Success = true, SourceIndex = 0 };
        }
        catch
        {
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

    private static void InjectJmespath(Engine engine)
    {
        engine.SetValue("jmespath", new Func<JsonNode?, string, object?>((data, query) =>
        {
            if (data is null) return null;

            // 简单路径查询: "foo.bar[0].baz"
            // 完整 JMESPath 需引入第三方库，当前实现基础路径导航
            // 统一返回 JSON 字符串，避免标量与对象返回类型不一致
            try
            {
                var result = NavigateJsonPath(data, query.AsSpan());
                return result?.ToJsonString();
            }
            catch
            {
                return null;
            }
        }));
    }

    private static JsonNode? NavigateJsonPath(JsonNode node, ReadOnlySpan<char> path)
    {
        if (path.Length == 0) return node;

        var current = node;
        var remaining = path;

        while (remaining.Length > 0)
        {
            remaining = remaining.TrimStart('.');

            if (remaining.Length == 0) break;

            // 数组索引: [0]
            if (remaining[0] == '[')
            {
                var end = remaining.IndexOf(']');
                if (end < 0) return null;

                if (current is JsonArray arr)
                {
                    var indexStr = remaining[1..end].ToString();
                    if (int.TryParse(indexStr, out var idx) && idx >= 0 && idx < arr.Count)
                    {
                        current = arr[idx];
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }

                remaining = remaining[(end + 1)..];
                continue;
            }

            // 属性名
            var dotOrBracket = remaining.IndexOfAny('.', '[');
            var key = dotOrBracket < 0
                ? remaining.ToString()
                : remaining[..dotOrBracket].ToString();

            if (current is JsonObject obj && obj.TryGetPropertyValue(key, out var child))
            {
                current = child;
            }
            else
            {
                return null;
            }

            remaining = dotOrBracket < 0 ? [] : remaining[dotOrBracket..];
        }

        return current;
    }
}
