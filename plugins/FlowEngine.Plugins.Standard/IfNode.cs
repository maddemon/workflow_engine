using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 条件分支节点，根据条件表达式路由到 true 或 false 分支。
/// 支持多个条件组合（AND/OR）。
/// </summary>
public sealed class IfNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "if";

    /// <inheritdoc />
    public string DisplayName => "If";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "shuffle";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 条件组合方式。
    /// </summary>
    [Description("How to combine multiple conditions.")]
    public IfCombinator Combinator { get; set; } = IfCombinator.And;

    /// <summary>
    /// 条件列表。
    /// </summary>
    [Description("Conditions to evaluate. All conditions must match for true output (with AND).")]
    public List<IfCondition> Conditions { get; set; } = [];

    /// <summary>
    /// 忽略大小写。
    /// </summary>
    [Description("Whether to ignore case when comparing strings.")]
    public bool IgnoreCase { get; set; } = true;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input", DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "true", DisplayName = "True", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = "false", DisplayName = "False", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (Conditions.Count == 0)
            {
                return Task.FromResult(context.ErrorResult("MissingConditions", "At least one condition is required."));
            }

            var conditionResult = EvaluateConditions(context);

            var inputBatch = context.Inputs.TryGetValue("input", out var batch)
                ? batch
                : new DataBatch();

            return Task.FromResult(new NodeExecutionResult
            {
                Success = true,
                Output = inputBatch,
                BranchIndex = conditionResult ? 0 : 1
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(context.ErrorResult("ConditionError", $"Condition evaluation failed: {ex.Message}"));
        }
    }

    private bool EvaluateConditions(NodeExecutionContext context)
    {
        var results = Conditions.Select(c => EvaluateCondition(c, context)).ToList();

        return Combinator == IfCombinator.And
            ? results.All(r => r)
            : results.Any(r => r);
    }

    private bool EvaluateCondition(IfCondition condition, NodeExecutionContext context)
    {
        var leftValue = ResolveValue(condition.LeftValue, context);
        var rightValue = condition.RightValue;

        return condition.Operation switch
        {
            IfOperation.Equals => CompareValues(leftValue, rightValue) == 0,
            IfOperation.NotEquals => CompareValues(leftValue, rightValue) != 0,
            IfOperation.GreaterThan => CompareValues(leftValue, rightValue) > 0,
            IfOperation.GreaterThanOrEquals => CompareValues(leftValue, rightValue) >= 0,
            IfOperation.LessThan => CompareValues(leftValue, rightValue) < 0,
            IfOperation.LessThanOrEquals => CompareValues(leftValue, rightValue) <= 0,
            IfOperation.Contains => leftValue?.Contains(rightValue ?? string.Empty, IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ?? false,
            IfOperation.StartsWith => leftValue?.StartsWith(rightValue ?? string.Empty, IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ?? false,
            IfOperation.EndsWith => leftValue?.EndsWith(rightValue ?? string.Empty, IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ?? false,
            IfOperation.IsEmpty => string.IsNullOrEmpty(leftValue),
            IfOperation.IsNotEmpty => !string.IsNullOrEmpty(leftValue),
            _ => false
        };
    }

    private string? ResolveValue(string? value, NodeExecutionContext context)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        // Use Jint to evaluate JS expression
        try
        {
            var inputData = context.InputData;
            using var js = FlowEngine.Runtime.Scripting.JsEngine.Create();

            js.SetValue("input", inputData);
            var result = js.Evaluate(value);
            return result?.ToString();
        }
        catch
        {
            // If Jint fails, return the raw value
            return value;
        }
    }

    private int CompareValues(string? left, string? right)
    {
        if (left is null && right is null) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        // Try numeric comparison
        if (double.TryParse(left, out var leftNum) && double.TryParse(right, out var rightNum))
        {
            return leftNum.CompareTo(rightNum);
        }

        return IgnoreCase
            ? string.Compare(left, right, StringComparison.OrdinalIgnoreCase)
            : string.Compare(left, right, StringComparison.Ordinal);
    }
}

/// <summary>
/// If 节点的条件定义。
/// </summary>
public sealed class IfCondition
{
    /// <summary>
    /// 左值（字段路径或表达式）。
    /// </summary>
    public string LeftValue { get; set; } = string.Empty;

    /// <summary>
    /// 比较操作。
    /// </summary>
    public IfOperation Operation { get; set; } = IfOperation.Equals;

    /// <summary>
    /// 右值。
    /// </summary>
    public string RightValue { get; set; } = string.Empty;
}

/// <summary>
/// If 操作类型。
/// </summary>
public enum IfOperation
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
public enum IfCombinator
{
    And,
    Or
}
