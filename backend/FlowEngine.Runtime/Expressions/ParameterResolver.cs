using FlowEngine.Runtime.Scripting;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Expressions;

/// <summary>
/// 节点参数解析器，对字符串参数执行 JavaScript 表达式求值。
/// 非表达式字符串保持原样返回。
/// </summary>
public sealed class ParameterResolver
{
    private readonly ILogger<ParameterResolver> _logger;

    /// <summary>
    /// 初始化 <see cref="ParameterResolver"/>。
    /// </summary>
    public ParameterResolver(ILogger<ParameterResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 解析参数字典，对字符串值中的表达式进行求值。
    /// </summary>
    /// <param name="rawParameters">原始参数字典。</param>
    /// <param name="jsEngine">JsEngine 实例（已设置上下文变量）。</param>
    /// <returns>解析后的参数字典。</returns>
    public Dictionary<string, object> Resolve(
        IReadOnlyDictionary<string, object> rawParameters,
        JsEngine jsEngine)
    {
        ArgumentNullException.ThrowIfNull(rawParameters);
        ArgumentNullException.ThrowIfNull(jsEngine);

        var resolved = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in rawParameters)
        {
            try
            {
                resolved[key] = ResolveValue(value!, jsEngine);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "参数 {ParameterName} 的表达式求值失败", key);
                throw;
            }
        }

        return resolved;
    }

    private object ResolveValue(object value, JsEngine jsEngine)
    {
        if (value is string str)
        {
            return IsExpression(str)
                ? JsEngine.ToClrValue(jsEngine.Evaluate(str)) ?? string.Empty
                : str;
        }

        if (value is System.Text.Json.JsonElement element)
        {
            return element.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => ResolveValue(element.GetString() ?? string.Empty, jsEngine),
                System.Text.Json.JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.GetDouble(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => null!,
                _ => element.GetRawText(),
            };
        }

        if (value is IEnumerable<KeyValuePair<string, object>> dict && value is not string)
        {
            return dict.ToDictionary(
                x => x.Key,
                x => ResolveValue(x.Value!, jsEngine),
                StringComparer.OrdinalIgnoreCase);
        }

        if (value is IEnumerable<object> list && value is not string)
        {
            return list.Select(item => ResolveValue(item!, jsEngine)).ToList();
        }

        return value!;
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

    private static readonly HashSet<string> s_knownIdentifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "input", "inputs", "nodes", "items", "parameter",
        "workflow", "execution", "env", "runIndex", "run_index",
        "this", "true", "false", "null", "undefined",
        "console", "fetch"
    };

    private static string GetFirstWord(ReadOnlySpan<char> text)
    {
        var i = 0;
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
        {
            i++;
        }
        return text[..i].ToString();
    }
}
