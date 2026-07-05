using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 过滤节点，根据条件保留或丢弃数据项。
/// </summary>
public sealed class FilterNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "filter";

    /// <inheritdoc />
    public string DisplayName => "Filter";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "filter";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 过滤条件（表达式）。
    /// </summary>
    [Description("Condition expression. Items matching the condition are kept.")]
    [Hint(PresentationHint.TextArea)]
    public string Condition { get; set; } = string.Empty;

    /// <summary>
    /// 条件组合方式。
    /// </summary>
    [Description("How to combine multiple conditions.")]
    public FilterCombinator Combinator { get; set; } = FilterCombinator.And;

    /// <summary>
    /// 额外条件列表。
    /// </summary>
    [Description("Additional conditions to combine.")]
    public List<FilterCondition> Conditions { get; set; } = [];

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input", DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "kept", DisplayName = "Kept", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = "discarded", DisplayName = "Discarded", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var inputBatch = context.Inputs.TryGetValue("input", out var batch)
            ? batch
            : new DataBatch();

        var keptItems = new List<DataItem>();
        var discardedItems = new List<DataItem>();

        foreach (var item in inputBatch.Items)
        {
            var matches = EvaluateCondition(item.Data, context);
            if (matches)
            {
                keptItems.Add(item);
            }
            else
            {
                discardedItems.Add(item);
            }
        }

        // For now, return kept items as output
        // In a full implementation, we'd need separate output ports
        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = keptItems }
        });
    }

    private bool EvaluateCondition(JsonNode? data, NodeExecutionContext context)
    {
        // If there's a main condition, evaluate it
        if (!string.IsNullOrWhiteSpace(Condition))
        {
            var mainResult = EvaluateExpression(Condition, data, context);
            if (Conditions.Count == 0)
            {
                return mainResult;
            }

            if (Combinator == FilterCombinator.And && !mainResult)
            {
                return false;
            }
            if (Combinator == FilterCombinator.Or && mainResult)
            {
                return true;
            }
        }

        // Evaluate additional conditions
        if (Conditions.Count == 0)
        {
            return true; // No conditions, keep all
        }

        var results = Conditions.Select(c => EvaluateCondition(c, data, context)).ToList();

        return Combinator == FilterCombinator.And
            ? results.All(r => r)
            : results.Any(r => r);
    }

    private bool EvaluateCondition(FilterCondition condition, JsonNode? data, NodeExecutionContext context)
    {
        var leftValue = GetJsonValue(data, condition.LeftValue);
        var rightValue = condition.RightValue;

        return condition.Operation switch
        {
            FilterOperation.Equals => CompareValues(leftValue, rightValue, condition.IgnoreCase) == 0,
            FilterOperation.NotEquals => CompareValues(leftValue, rightValue, condition.IgnoreCase) != 0,
            FilterOperation.GreaterThan => CompareValues(leftValue, rightValue, condition.IgnoreCase) > 0,
            FilterOperation.GreaterThanOrEquals => CompareValues(leftValue, rightValue, condition.IgnoreCase) >= 0,
            FilterOperation.LessThan => CompareValues(leftValue, rightValue, condition.IgnoreCase) < 0,
            FilterOperation.LessThanOrEquals => CompareValues(leftValue, rightValue, condition.IgnoreCase) <= 0,
            FilterOperation.Contains => leftValue?.Contains(rightValue ?? string.Empty, StringComparison.OrdinalIgnoreCase) ?? false,
            FilterOperation.StartsWith => leftValue?.StartsWith(rightValue ?? string.Empty, StringComparison.OrdinalIgnoreCase) ?? false,
            FilterOperation.EndsWith => leftValue?.EndsWith(rightValue ?? string.Empty, StringComparison.OrdinalIgnoreCase) ?? false,
            FilterOperation.IsEmpty => string.IsNullOrEmpty(leftValue),
            FilterOperation.IsNotEmpty => !string.IsNullOrEmpty(leftValue),
            _ => false
        };
    }

    private static bool EvaluateExpression(string expression, JsonNode? data, NodeExecutionContext context)
    {
        // Simple expression evaluation
        // For now, just check if the expression resolves to a truthy value
        // A full implementation would parse {{ $json.field }} syntax
        if (expression.StartsWith("{{") && expression.EndsWith("}}"))
        {
            var fieldPath = expression[2..^2].Trim();
            if (fieldPath.StartsWith("$json."))
            {
                var path = fieldPath[6..];
                var value = GetJsonValue(data, path);
                return !string.IsNullOrEmpty(value) && value != "false" && value != "0";
            }
        }

        // Check for direct boolean expressions
        var trimmed = expression.Trim();
        if (bool.TryParse(trimmed, out var boolResult))
        {
            return boolResult;
        }

        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;

        // For non-empty strings, return true
        return !string.IsNullOrEmpty(trimmed);
    }

    private static string? GetJsonValue(JsonNode? data, string? path)
    {
        if (data is null || string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (data is not JsonObject obj)
        {
            return null;
        }

        var parts = path.Split('.');
        JsonNode? current = obj;

        foreach (var part in parts)
        {
            if (current is JsonObject currentObj && currentObj.TryGetPropertyValue(part, out var next))
            {
                current = next;
            }
            else
            {
                return null;
            }
        }

        return current?.ToString();
    }

    private static int CompareValues(string? left, string? right, bool ignoreCase)
    {
        if (left is null && right is null) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        var comparison = ignoreCase
            ? string.Compare(left, right, StringComparison.OrdinalIgnoreCase)
            : string.Compare(left, right, StringComparison.Ordinal);

        return comparison;
    }
}

/// <summary>
/// 过滤条件。
/// </summary>
public sealed class FilterCondition
{
    /// <summary>
    /// 左值字段路径。
    /// </summary>
    public string LeftValue { get; set; } = string.Empty;

    /// <summary>
    /// 比较操作。
    /// </summary>
    public FilterOperation Operation { get; set; } = FilterOperation.Equals;

    /// <summary>
    /// 右值。
    /// </summary>
    public string RightValue { get; set; } = string.Empty;

    /// <summary>
    /// 是否忽略大小写。
    /// </summary>
    public bool IgnoreCase { get; set; } = true;
}

/// <summary>
/// 过滤操作类型。
/// </summary>
public enum FilterOperation
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEquals,
    LessThan,
    LessThanOrEquals,
    Contains,
    StartsWith,
    EndsWith,
    IsEmpty,
    IsNotEmpty
}

/// <summary>
/// 条件组合方式。
/// </summary>
public enum FilterCombinator
{
    And,
    Or
}
