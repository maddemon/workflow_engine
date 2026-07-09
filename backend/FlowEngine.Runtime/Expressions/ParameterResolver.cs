using System.Text.Json;
using System.Text.RegularExpressions;
using FlowEngine.Core.Expressions;
using FlowEngine.Runtime.Expressions.Exceptions;
using FlowEngine.Core.Scripting;
using Jint;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Expressions;

using PreparedScript = Jint.Prepared<Acornima.Ast.Script>;

/// <summary>
/// 节点参数解析器，对字符串参数执行 JavaScript 表达式求值。
/// 非表达式字符串保持原样返回。
/// </summary>
public sealed class ParameterResolver
{
    private static readonly TimeSpan DefaultCacheExpiration = TimeSpan.FromHours(1);
    private static readonly HashSet<string> s_knownIdentifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        // 旧式裸全局（向后兼容，后续实现按 plan-004 收敛到 $ 前缀）
        "input", "inputs", "nodes", "items", "parameter",
        "workflow", "execution", "env", "runIndex", "run_index",
        "this", "true", "false", "null", "undefined",
        "now", "nowIso", "jmespath", "length", "trim",
        // n8n 式 $ 前缀内建（plan-004 评审5：命名铁律 $ 前缀=引擎内建）
        "$json", "$input", "$items", "$node",
        "$workflow", "$execution", "$env", "$vars", "$now", "$today",
        "$runIndex", "$itemIndex", "$credentials", "$ctx",
        // 节点/场景特有
        "$cursor", "$nextCursor", "$page", "$response", "$payload", "$tool"
    };

    private static readonly HashSet<string> s_forbiddenIdentifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "require", "process", "fs", "path", "os", "net", "http", "https",
        "fetch", "XMLHttpRequest", "WebSocket", "eval",
        "setTimeout", "setInterval", "setImmediate", "clearTimeout", "clearInterval",
        "globalThis", "window", "document", "constructor", "prototype", "__proto__",
        "import", "export", "module", "exports"
    };

    private readonly ILogger<ParameterResolver> _logger;
    private readonly IMemoryCache? _cache;

    /// <summary>
    /// 初始化 <see cref="ParameterResolver"/>。
    /// </summary>
    public ParameterResolver(ILogger<ParameterResolver> logger, IMemoryCache? cache = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = cache;
    }

    /// <summary>
    /// 解析参数字典，对字符串值中的表达式进行求值。
    /// </summary>
    /// <param name="rawParameters">原始参数字典。</param>
    /// <param name="jsEngine">JsEngine 实例（已设置上下文变量）。</param>
    /// <param name="cacheKey">可选的表达式缓存键（不含具体表达式文本）。</param>
    /// <returns>解析后的参数字典。</returns>
    public Dictionary<string, object> Resolve(
        IReadOnlyDictionary<string, object> rawParameters,
        JsEngine jsEngine,
        ExpressionCacheKey? cacheKey = null)
    {
        ArgumentNullException.ThrowIfNull(rawParameters);
        ArgumentNullException.ThrowIfNull(jsEngine);

        var resolved = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in rawParameters)
        {
            try
            {
                resolved[key] = ResolveValue(value!, jsEngine, cacheKey);
            }
            catch (ExpressionEvaluationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "参数 {ParameterName} 的表达式求值失败", key);
                throw;
            }
        }

        return resolved;
    }

    private object ResolveValue(object value, JsEngine jsEngine, ExpressionCacheKey? cacheKey)
    {
        if (value is string str)
        {
            return IsExpression(str)
                ? EvaluateExpression(str, jsEngine, cacheKey)
                : str;
        }

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => ResolveValue(element.GetString() ?? string.Empty, jsEngine, cacheKey),
                JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null or JsonValueKind.Undefined => null!,
                _ => element.GetRawText(),
            };
        }

        if (value is IEnumerable<KeyValuePair<string, object>> dict && value is not string)
        {
            return dict.ToDictionary(
                x => x.Key,
                x => ResolveValue(x.Value!, jsEngine, cacheKey),
                StringComparer.OrdinalIgnoreCase);
        }

        if (value is IEnumerable<object> list && value is not string)
        {
            return list.Select(item => ResolveValue(item!, jsEngine, cacheKey)).ToList();
        }

        return value!;
    }

    private object EvaluateExpression(string expression, JsEngine jsEngine, ExpressionCacheKey? cacheKey)
    {
        ValidateSecurity(expression);

        try
        {
            var prepared = GetOrPrepare(expression, cacheKey);
            var result = jsEngine.EvaluatePrepared(prepared);
            return JsEngine.ToClrValue(result) ?? string.Empty;
        }
        catch (ExpressionEvaluationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw WrapException(expression, ex);
        }
    }

    private PreparedScript GetOrPrepare(string expression, ExpressionCacheKey? cacheKey)
    {
        if (cacheKey is not null && _cache is not null)
        {
            var key = cacheKey with { Expression = expression };
            if (_cache.TryGetValue(key, out PreparedScript prepared))
            {
                return prepared;
            }

            prepared = JsEngine.PrepareExpression(expression);
            _cache.Set(key, prepared, DefaultCacheExpiration);
            return prepared;
        }

        return JsEngine.PrepareExpression(expression);
    }

    private static void ValidateSecurity(string expression)
    {
        foreach (var identifier in s_forbiddenIdentifiers)
        {
            if (ContainsWord(expression, identifier))
            {
                throw new SecurityViolationException(expression, $"表达式包含禁止使用的标识符 '{identifier}'");
            }
        }
    }

    private static bool ContainsWord(string text, string word)
    {
        // 逐字符扫描，跳过字符串/模板字面量与注释内容，
        // 避免把 "http://..." 这类字面量中的子串误判为禁止标识符。
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

            // 行注释：跳过至行尾
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }

            // 块注释：跳过至 */
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

    private static ExpressionEvaluationException WrapException(string expression, Exception ex)
    {
        var message = ex.Message ?? ex.GetType().Name;

        if (ex.GetType().Name.Contains("ParseError", StringComparison.OrdinalIgnoreCase)
            || ex.GetType().Name.Contains("SyntaxError", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Unexpected token", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Unexpected end", StringComparison.OrdinalIgnoreCase))
        {
            return new SyntaxErrorException(expression, $"语法错误: {message}", ex);
        }

        if (message.Contains("is not defined", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Cannot read properties of undefined", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Cannot read property", StringComparison.OrdinalIgnoreCase))
        {
            var fieldName = TryExtractMissingName(message) ?? "<unknown>";
            return new FieldNotFoundException(expression, fieldName, null, ex);
        }

        if (message.Contains("Cannot convert", StringComparison.OrdinalIgnoreCase)
            || message.Contains("is not a function", StringComparison.OrdinalIgnoreCase)
            || message.Contains("is not iterable", StringComparison.OrdinalIgnoreCase))
        {
            return new TypeMismatchException(expression, $"类型不匹配: {message}", ex);
        }

        return new SyntaxErrorException(expression, $"求值失败: {message}", ex);
    }

    private static string? TryExtractMissingName(string message)
    {
        // Jint "xxx is not defined"
        var match = Regex.Match(message, @"^\s*([\w$]+)\s+is not defined", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        // "Cannot read properties of undefined (reading 'xxx')"
        match = Regex.Match(message, @"reading ['""]([\w$]+)['""]", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return null;
    }

    private static bool IsExpression(string str)
    {
        if (string.IsNullOrWhiteSpace(str)) return false;

        var trimmed = str.AsSpan().TrimStart();
        if (trimmed.Length == 0) return false;

        if (trimmed is "true" or "false" or "null") return true;

        if (int.TryParse(trimmed, out _) || long.TryParse(trimmed, out _) || double.TryParse(trimmed, out _))
            return true;

        var firstWord = GetFirstWord(trimmed);
        if (s_knownIdentifiers.Contains(firstWord)) return true;

        foreach (var ch in trimmed)
        {
            if (ch is '=' or '+' or '-' or '*' or '/' or '%' or '>' or '<' or '!' or '?' or ':' or '(' or ')' or '[' or ']' or '&' or '|')
            {
                return true;
            }
        }

        return false;
    }

    private static string GetFirstWord(ReadOnlySpan<char> text)
    {
        var i = 0;
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '$'))
        {
            i++;
        }
        return text[..i].ToString();
    }
}
