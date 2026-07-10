using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Acornima;
using FlowEngine.Core.Exceptions;
using Microsoft.Extensions.Options;

namespace FlowEngine.Core.Scripting;

using JintPreparedScript = Jint.Prepared<Acornima.Ast.Script>;

/// <summary>
/// 基于 SHA256(Source) 的脚本编译产物缓存实现。
/// </summary>
public sealed class ScriptCache : IScriptCache
{
    private readonly ConcurrentDictionary<string, JintPreparedScript> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _insertionOrder = [];
    private readonly object _trimLock = new();
    private readonly JsEngineOptions _options;

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

        ValidateSecurity(script);

        var key = ComputeCacheKey(script.Source);
        ScriptErrorException? compileError = null;
        JintPreparedScript? prepared = null;

        try
        {
            prepared = _cache.GetOrAdd(key, _ => ScriptCompiler.Compile(script));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            compileError = new ScriptErrorException(script, ex.Message, ex);
        }

        if (prepared is not null)
        {
            lock (_trimLock)
            {
                if (!_insertionOrder.Contains(key))
                {
                    _insertionOrder.Add(key);
                }
            }
        }

        return new PreparedScript(script, key, prepared ?? CompileNoOp(), compileError);
    }

    /// <inheritdoc />
    public void TrimIfNeeded(int maxItems)
    {
        if (maxItems <= 0)
        {
            _cache.Clear();
            lock (_trimLock)
            {
                _insertionOrder.Clear();
            }

            return;
        }

        lock (_trimLock)
        {
            while (_insertionOrder.Count > maxItems)
            {
                var oldest = _insertionOrder[0];
                _insertionOrder.RemoveAt(0);
                _cache.TryRemove(oldest, out _);
            }
        }
    }

    internal static string ComputeCacheKey(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static JintPreparedScript CompileNoOp()
    {
        return ScriptCompiler.Compile(new Script { Source = string.Empty });
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
