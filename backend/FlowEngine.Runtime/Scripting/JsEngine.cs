using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Scripting;

/// <summary>
/// Jint JavaScript 引擎封装。
/// 提供表达式求值（参数、条件）和脚本执行（JSNode/CodeSnippet），
/// 内置 console/fetch polyfill 和安全沙箱。
/// </summary>
public sealed class JsEngine : IDisposable
{
    private readonly Engine _engine;
    private readonly HttpClient _httpClient;
    private readonly ILogger? _logger;
    private bool _disposed;

    private JsEngine(Engine engine, HttpClient httpClient, ILogger? logger)
    {
        _engine = engine;
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// 创建 JsEngine 实例。每个实例有独立的沙箱和 HttpClient。
    /// </summary>
    /// <param name="configure">可选的 Engine 选项配置回调。</param>
    /// <param name="logger">可选的日志记录器。</param>
    public static JsEngine Create(Action<Options>? configure = null, ILogger? logger = null)
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

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
        InjectFetch(engine, httpClient, logger);

        return new JsEngine(engine, httpClient, logger);
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
            _httpClient.Dispose();
            _engine.Dispose();
        }
    }

    private static void InjectConsole(Engine engine, ILogger? logger)
    {
        engine.SetValue("console", new
        {
            log = new Action<object?[]?>(args =>
            {
                var msg = args is { Length: > 0 } ? string.Join(" ", args) : "";
                logger?.LogInformation("[JS] {Msg}", msg);
            }),
            info = new Action<object?[]?>(args =>
            {
                var msg = args is { Length: > 0 } ? string.Join(" ", args) : "";
                logger?.LogInformation("[JS] {Msg}", msg);
            }),
            warn = new Action<object?[]?>(args =>
            {
                var msg = args is { Length: > 0 } ? string.Join(" ", args) : "";
                logger?.LogWarning("[JS] {Msg}", msg);
            }),
            error = new Action<object?[]?>(args =>
            {
                var msg = args is { Length: > 0 } ? string.Join(" ", args) : "";
                logger?.LogError("[JS] {Msg}", msg);
            }),
        });
    }

    private static void InjectFetch(Engine engine, HttpClient httpClient, ILogger? logger)
    {
        engine.SetValue("fetch", new Func<string, object?[]?, Task<object?>>(async (url, options) =>
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                if (options is { Length: > 0 } && options[0] is not null)
                {
                    if (options[0] is JsonObject opts)
                    {
                        var method = opts["method"]?.GetValue<string>() ?? "GET";
                        request.Method = new HttpMethod(method);

                        if (opts.TryGetPropertyValue("headers", out var headersNode) && headersNode is JsonObject headers)
                        {
                            foreach (var (key, value) in headers)
                            {
                                request.Headers.TryAddWithoutValidation(key, value?.GetValue<string>());
                            }
                        }

                        if (opts.TryGetPropertyValue("body", out var bodyNode) && bodyNode is not null)
                        {
                            request.Content = new StringContent(bodyNode.ToJsonString(), Encoding.UTF8, "application/json");
                        }
                    }
                    else
                    {
                        var bodyText = options[0]?.ToString();
                        if (bodyText is not null)
                        {
                            request.Content = new StringContent(bodyText, Encoding.UTF8, "application/json");
                        }
                    }
                }

                var response = await httpClient.SendAsync(request).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                try { return JsonSerializer.Deserialize<JsonElement>(text); }
                catch { return text; }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "[JS fetch] 请求失败: {Url}", url);
                throw;
            }
        }));
    }
}
