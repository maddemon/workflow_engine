using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Acornima;
using FlowEngine.Core.Exceptions;
using Microsoft.Extensions.Options;

namespace FlowEngine.Core.Scripting;

using JintPreparedScript = Jint.Prepared<Acornima.Ast.Script>;

/// <summary>
/// 基于 SHA256(Source) 的脚本编译产物缓存实现。
/// <para>
/// 容量保护：当条目数超过 <see cref="DefaultMaxCapacity"/> 时，按加入顺序（LRU）淘汰最旧条目，
/// 避免 <see cref="ConcurrentDictionary"/> 无界增长。安全校验仅在首次编译（缓存未命中）时执行一次。
/// </para>
/// </summary>
public sealed class ScriptCache
{
    /// <summary>
    /// 缓存条目容量上限，超过则按加入顺序淘汰最旧条目。
    /// </summary>
    public const int DefaultMaxCapacity = 4096;

    private readonly ConcurrentDictionary<string, Lazy<JintPreparedScript>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ScriptErrorException> _compileErrors = new(StringComparer.OrdinalIgnoreCase);

    // 加入顺序跟踪：LinkedList 提供 O(1) 头尾增删，Dictionary 提供 O(1) 成员判定，避免 List.Contains 的 O(n) 扫描。
    private readonly LinkedList<string> _order = new();
    private readonly Dictionary<string, LinkedListNode<string>> _orderIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _trimLock = new();

    private readonly JsEngineOptions _options;

    private static readonly Lazy<JintPreparedScript> s_noOp = new(() => ScriptCompiler.Compile(new Script { Source = string.Empty }));

    /// <summary>
    /// 初始化 <see cref="ScriptCache"/>。
    /// </summary>
    public ScriptCache(IOptions<JsEngineOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public PreparedScript GetOrPrepare(Script script)
    {
        ArgumentNullException.ThrowIfNull(script);

        var key = ComputeCacheKey(script.Source);

        // 命中已缓存的编译错误：直接返回，避免重复 tokenize / 编译。
        if (_compileErrors.TryGetValue(key, out var cachedError))
        {
            return new PreparedScript(script, key, s_noOp.Value, cachedError);
        }

        JintPreparedScript? prepared = null;
        ScriptErrorException? compileError = null;

        // 使用 Lazy<T> 模式确保编译只执行一次，避免竞态条件
        var lazy = _cache.GetOrAdd(key, _ =>
        {
            return new Lazy<JintPreparedScript>(() =>
            {
                // 安全校验仅在首次编译（缓存未命中）时执行一次
                ValidateSecurity(script);
                return ScriptCompiler.Compile(script);
            });
        });

        try
        {
            prepared = lazy.Value;
        }
        catch (ScriptSecurityException)
        {
            // 安全校验异常必须透传，绝不能被当作编译错误吞掉（否则沙箱校验失效）。
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            compileError = new ScriptErrorException(script, ex.Message, ex);
            _compileErrors.GetOrAdd(key, compileError);
            // 编译失败时从缓存移除，避免后续请求继续使用失败的 Lazy
            _cache.TryRemove(key, out _);
        }

        if (prepared is not null)
        {
            RecordAccess(key);
        }

        return new PreparedScript(script, key, prepared ?? s_noOp.Value, compileError);
    }

    /// <summary>
    /// 记录最近访问的键并触发容量保护（LRU 淘汰）。
    /// </summary>
    private void RecordAccess(string key)
    {
        lock (_trimLock)
        {
            if (_orderIndex.TryGetValue(key, out var node))
            {
                // 已存在：移动到队尾表示最近使用。
                _order.Remove(node);
                _order.AddLast(node);
            }
            else
            {
                _orderIndex[key] = _order.AddLast(key);
            }

            while (_order.Count > DefaultMaxCapacity)
            {
                EvictOldest();
            }
        }
    }

    /// <summary>
    /// 淘汰队首（最旧）条目，并同步清理缓存与编译错误表。
    /// </summary>
    private void EvictOldest()
    {
        var oldest = _order.First;
        if (oldest is null)
        {
            return;
        }

        _order.RemoveFirst();
        _orderIndex.Remove(oldest.Value);
        _cache.TryRemove(oldest.Value, out _);
        _compileErrors.TryRemove(oldest.Value, out _);
    }

    /// <inheritdoc />
    public void TrimIfNeeded(int maxItems)
    {
        if (maxItems <= 0)
        {
            // Q-7：所有集合的清空必须在同一把锁内完成，避免与 RecordAccess/EvictOldest 并发
            // 导致 _order 与 _cache/_compileErrors 状态不一致（_cache 已清空但 _order 仍引用旧键）。
            lock (_trimLock)
            {
                _cache.Clear();
                _compileErrors.Clear();
                _order.Clear();
                _orderIndex.Clear();
            }

            return;
        }

        lock (_trimLock)
        {
            while (_order.Count > maxItems)
            {
                EvictOldest();
            }
        }
    }

    internal static string ComputeCacheKey(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private void ValidateSecurity(Script script)
    {
        var source = script.Source;
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        var tokenizer = new Tokenizer(source);
        var context = new TokenizerContext();

        try
        {
            while (true)
            {
                tokenizer.Next(in context);
                var token = tokenizer.Current;
                if (token.Kind == TokenKind.EOF)
                {
                    break;
                }

                if (token.Kind is TokenKind.Identifier or TokenKind.Keyword
                    && token.Value is string name)
                {
                    foreach (var identifier in _options.ForbiddenIdentifiers)
                    {
                        if (name.Equals(identifier, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new ScriptSecurityException(script, identifier);
                        }
                    }
                }
            }
        }
        catch (ScriptErrorException)
        {
            throw;
        }
        catch (ParseErrorException)
        {
            // 源码语法错误时 tokenizer 可能抛出异常；安全校验只关注真实标识符，
            // 语法错误交给后续编译阶段处理。
        }
    }
}


