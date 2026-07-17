using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Expressions;
using FlowEngine.Core.Scripting;
using FlowEngine.Runtime.Expressions.Exceptions;
using Jint;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowEngine.Runtime.Expressions;

/// <summary>
/// 节点参数解析器，对字符串参数中的旧式表达式进行求值。
/// Script 类型参数由 <see cref="NodeExecutionContextFactory"/> 统一预求值，
/// 本类仅保留对遗留字符串参数的兼容处理。
/// </summary>
public sealed class ParameterResolver
{
    private static readonly HashSet<string> s_knownIdentifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        // $ 前缀内建（plan-004 评审5：命名铁律 $ 前缀=引擎内建）
        "$json", "$input", "$items", "$node",
        "$workflow", "$execution", "$env", "$vars", "$now", "$today",
        "$runIndex", "$itemIndex", "$credentials", "$ctx",
        // 节点/场景特有
        "$cursor", "$nextCursor", "$page", "$response", "$payload", "$tool"
    };

    // 固定值形态：GUID / URL 不应被当作表达式求值（避免 credentialId、url 等被误判）
    private static readonly Regex s_guidRegex = new(
        @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled);

    private readonly ILogger<ParameterResolver> _logger;
    private readonly ScriptCache _scriptCache;

    /// <summary>
    /// 初始化 <see cref="ParameterResolver"/>。
    /// </summary>
    public ParameterResolver(
        ILogger<ParameterResolver> logger,
        IOptions<JsEngineOptions> options,
        ScriptCache scriptCache)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scriptCache);
        _ = options.Value; // 确保配置可用；安全策略由 ScriptCache 在编译时读取
        _scriptCache = scriptCache;
    }

    /// <summary>
    /// 解析参数字典，对字符串值中的旧式表达式进行求值。
    /// </summary>
    /// <param name="rawParameters">原始参数字典。</param>
    /// <param name="jsEngine">JsEngine 实例（已设置上下文变量）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解析后的参数字典。</returns>
    public async Task<Dictionary<string, object>> ResolveAsync(
        IReadOnlyDictionary<string, object> rawParameters,
        JsEngine jsEngine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawParameters);
        ArgumentNullException.ThrowIfNull(jsEngine);

        var resolved = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in rawParameters)
        {
            try
            {
                resolved[key] = await ResolveValueAsync(value!, jsEngine, cancellationToken).ConfigureAwait(false);
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

    private async Task<object> ResolveValueAsync(object value, JsEngine jsEngine, CancellationToken cancellationToken)
    {
        if (value is string str)
        {
            return IsExpression(str)
                ? await EvaluateExpressionAsync(str, jsEngine, cancellationToken).ConfigureAwait(false)
                : str;
        }

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => await ResolveValueAsync(element.GetString() ?? string.Empty, jsEngine, cancellationToken).ConfigureAwait(false),
                JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null or JsonValueKind.Undefined => null!,
                _ => element.GetRawText(),
            };
        }

        if (value is IEnumerable<KeyValuePair<string, object>> dict && value is not string)
        {
            var resolvedDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, item) in dict)
            {
                resolvedDict[key] = await ResolveValueAsync(item!, jsEngine, cancellationToken).ConfigureAwait(false);
            }

            return resolvedDict;
        }

        if (value is IEnumerable<object> list && value is not string)
        {
            var resolvedList = new List<object>();
            foreach (var item in list)
            {
                resolvedList.Add(await ResolveValueAsync(item!, jsEngine, cancellationToken).ConfigureAwait(false));
            }

            return resolvedList;
        }

        return value!;
    }

    private async Task<object> EvaluateExpressionAsync(string expression, JsEngine jsEngine, CancellationToken cancellationToken)
    {
        var script = new Script
        {
            Source = expression,
            Language = ScriptLanguage.JavaScript,
            ReturnType = ScriptReturnType.String
        };

        try
        {
            var prepared = _scriptCache.GetOrPrepare(script);
            var scriptContext = new ScriptContext(new NodeExecutionContext());
            var result = await prepared.RunAsync(scriptContext, jsEngine, cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                throw WrapException(expression, result.Error?.InnerException ?? new Exception(result.Error?.Message ?? "脚本执行失败"));
            }

            return result.ToClr() ?? string.Empty;
        }
        catch (ScriptSecurityException ex)
        {
            throw new SecurityViolationException(expression, ex.Message, ex);
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

        // 现代约定：表达式必须含 $ 前缀内建变量（plan-004 评审5 命名铁律）
        var firstWord = GetFirstWord(trimmed);
        if (s_knownIdentifiers.Contains(firstWord)) return true;

        // 扫描整个字符串，定位任意位置的 $xxx 内建变量（如 "url" + $credentials.x）
        for (int i = 0; i < trimmed.Length - 1; i++)
        {
            if (trimmed[i] != '$') continue;
            int j = i + 1;
            while (j < trimmed.Length && (char.IsLetterOrDigit(trimmed[j]) || trimmed[j] == '_')) j++;
            if (j > i + 1 && s_knownIdentifiers.Contains(trimmed[i..j].ToString()))
            {
                return true;
            }
        }

        // 固定值（GUID / URL）直接作为字面量，不参与表达式求值
        if (s_guidRegex.IsMatch(trimmed.ToString())) return false;
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;

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
