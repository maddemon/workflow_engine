using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Expressions;

/// <summary>
/// 节点参数解析器，负责将原始参数中的表达式求值为最终值。
/// </summary>
public sealed class ParameterResolver
{
    private readonly ExpressionEvaluator _evaluator;
    private readonly ILogger<ParameterResolver> _logger;

    /// <summary>
    /// 初始化 <see cref="ParameterResolver"/>。
    /// </summary>
    /// <param name="evaluator">表达式求值器。</param>
    /// <param name="logger">日志记录器。</param>
    public ParameterResolver(ExpressionEvaluator evaluator, ILogger<ParameterResolver> logger)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 解析参数字典，对字符串值中的表达式进行求值。
    /// </summary>
    /// <param name="rawParameters">原始参数字典。</param>
    /// <param name="context">表达式求值上下文。</param>
    /// <returns>解析后的参数字典。</returns>
    public Dictionary<string, object> Resolve(
        IReadOnlyDictionary<string, object> rawParameters,
        ExpressionContext context)
    {
        ArgumentNullException.ThrowIfNull(rawParameters);
        ArgumentNullException.ThrowIfNull(context);

        var resolved = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in rawParameters)
        {
            try
            {
                resolved[key] = ResolveValue(value!, context);
            }
            catch (ExpressionEvaluationException ex)
            {
                _logger.LogWarning(
                    ex,
                    "参数 {ParameterName} 的表达式求值失败：{Reason}",
                    key,
                    ex.Error.Reason);
                throw;
            }
        }

        return resolved;
    }

    private object ResolveValue(object value, ExpressionContext context)
    {
        if (value is string template)
        {
            return _evaluator.Evaluate(template, context) ?? string.Empty;
        }

        // JsonElement from JSON deserialization — convert to appropriate .NET type
        if (value is System.Text.Json.JsonElement element)
        {
            return element.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => _evaluator.Evaluate(element.GetString() ?? string.Empty, context) ?? string.Empty,
                System.Text.Json.JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.GetDouble(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => null!,
                _ => element.GetRawText(),
            };
        }

        if (value is IEnumerable<KeyValuePair<string, object>> dictionary && value is not string)
        {
            return dictionary.ToDictionary(
                x => x.Key,
                x => ResolveValue(x.Value!, context),
                StringComparer.OrdinalIgnoreCase);
        }

        if (value is IEnumerable<object> list && value is not string)
        {
            return list.Select(item => ResolveValue(item!, context)).ToList();
        }

        return value!;
    }
}
