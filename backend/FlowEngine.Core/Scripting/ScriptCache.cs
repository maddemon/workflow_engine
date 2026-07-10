using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting.Models;
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

        lock (_trimLock)
        {
            if (!_insertionOrder.Contains(key))
            {
                _insertionOrder.Add(key);
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

        foreach (var identifier in _options.ForbiddenIdentifiers)
        {
            if (ContainsWord(source, identifier))
            {
                throw new ScriptErrorException(script, $"脚本包含禁止使用的标识符 '{identifier}'");
            }
        }
    }

    private static bool ContainsWord(string text, string word)
    {
        var inSingle = false;
        var inDouble = false;
        var inTemplate = false;
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (inSingle || inDouble || inTemplate)
            {
                if (c == '\\')
                {
                    i += 2;
                    continue;
                }

                if (inSingle && c == '\'') inSingle = false;
                else if (inDouble && c == '"') inDouble = false;
                else if (inTemplate && c == '`') inTemplate = false;
                i++;
                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/'))
                {
                    i++;
                }

                if (i < text.Length) i += 2;
                continue;
            }

            if (c == '\'') { inSingle = true; i++; continue; }
            if (c == '"') { inDouble = true; i++; continue; }
            if (c == '`') { inTemplate = true; i++; continue; }

            if (text.Length - i >= word.Length
                && string.Equals(text.Substring(i, word.Length), word, StringComparison.OrdinalIgnoreCase))
            {
                var before = i == 0 || !IsIdentifierChar(text[i - 1]);
                var after = i + word.Length == text.Length || !IsIdentifierChar(text[i + word.Length]);
                if (before && after)
                {
                    return true;
                }
            }

            i++;
        }

        return false;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';
}
