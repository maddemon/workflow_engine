using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
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

            // 正则字面量：/.../flags；跳过其内容避免误判禁止标识符。
            if (c == '/' && IsRegexStart(text, i))
            {
                i++; // 跳过起始 /
                while (i < text.Length && text[i] != '/')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i += 2;
                        continue;
                    }
                    i++;
                }

                if (i < text.Length) i++; // 跳过结束 /
                while (i < text.Length && IsRegexFlag(text[i])) i++; // 跳过 flags
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

    private static bool IsRegexStart(string text, int i)
    {
        if (i + 1 >= text.Length)
        {
            return false;
        }

        var next = text[i + 1];
        if (next == '/' || next == '*')
        {
            return false; // 注释，不是正则
        }

        // 向前查找前一个非空白字符，判断当前是否处于期望表达式的上下文。
        var j = i - 1;
        while (j >= 0 && char.IsWhiteSpace(text[j]))
        {
            j--;
        }

        if (j < 0)
        {
            return true;
        }

        var prev = text[j];

        // 这些操作符/标点之后通常开始一个表达式，因此 / 更可能是正则。
        if (prev is '(' or '[' or '{' or ',' or ':' or ';' or '?' or '!' or '~'
            or '+' or '-' or '*' or '%' or '|' or '&' or '^' or '<' or '>' or '=')
        {
            return true;
        }

        // 如果前一个是标识符，需要判断是否为期望表达式的关键字（如 return /.../）。
        if (IsIdentifierChar(prev))
        {
            var keywordStart = j;
            while (keywordStart >= 0 && IsIdentifierChar(text[keywordStart]))
            {
                keywordStart--;
            }

            var keyword = text.Substring(keywordStart + 1, j - keywordStart).AsSpan();
            return keyword.Equals("return", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("throw", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("case", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("yield", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("await", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("typeof", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("void", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("delete", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("new", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("instanceof", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("in", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("of", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("do", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("else", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsRegexFlag(char c)
    {
        return c is 'g' or 'i' or 'm' or 's' or 'u' or 'y' or 'd' or 'v';
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';
}
