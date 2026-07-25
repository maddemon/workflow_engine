using FlowEngine.Core;
using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Core.ValueObjects;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 过滤节点，根据条件保留或丢弃数据项。
/// <c>Condition</c> 为 <see cref="Script"/> 类型（Hint=Script），在逐项求值时支持 <c>$json</c>/<c>$input</c>/<c>$credentials</c> 等所有 <c>$</c> 前缀变量。
/// <c>Conditions</c> 列表为结构化条件（LeftValue/Operation/RightValue），独立于表达式求值。
/// </summary>
public sealed class FilterNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "filter";

    /// <inheritdoc />
    public string DisplayName => "Filter";

    /// <inheritdoc />
    public string Category => "Data";

    /// <inheritdoc />
    public string Icon => "filter";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 过滤条件表达式（逐项求值）。支持 <c>$json.field === 'value'</c>、<c>$input.item().count > 10</c> 等。
    /// 类型为 <see cref="Script"/>，由节点在执行时逐项求值（不经工厂预求值）。
    /// </summary>
    [Description("Condition expression evaluated per item (e.g. $json.status === 'active').")]
    [Hint(PresentationHint.Script)]
    public Script Condition { get; set; } = Script.Empty;

    /// <summary>
    /// 条件组合方式。
    /// </summary>
    [Description("How to combine multiple conditions.")]
    public FilterCombinator Combinator { get; set; } = FilterCombinator.And;

    /// <summary>
    /// 额外条件列表（结构化）。
    /// </summary>
    [Description("Additional structured conditions to combine.")]
    public List<FilterCondition> Conditions { get; set; } = [];

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Kept, DisplayName = "Kept", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Discarded, DisplayName = "Discarded", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var inputBatch = context.GetInputBatch();

        var keptItems = new List<DataItem>();
        var discardedItems = new List<DataItem>();

        for (var itemIndex = 0; itemIndex < inputBatch.Items.Count; itemIndex++)
        {
            var item = inputBatch.Items[itemIndex];
            var matches = await EvaluateItemConditionAsync(
                item.Data, itemIndex, context, cancellationToken).ConfigureAwait(false);
            if (matches)
            {
                keptItems.Add(item);
            }
            else
            {
                discardedItems.Add(item);
            }
        }

        return new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = keptItems },
            // 同时向 Kept / Discarded 两个输出端口分发，使下游分支节点（连到 Kept 或 Discarded）能被正确调度。
            // 旧的「单一 Output + BranchIndex」模型无法表达「一次向两个端口同时输出」，故用 PortOutputs 逐端口路由。
            PortOutputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Kept] = new DataBatch { Items = keptItems },
                [FlowConstants.PortNames.Discarded] = new DataBatch { Items = discardedItems },
            }
        };
    }

    /// <summary>
    /// 逐项求值：对单个数据项评估主条件 + 结构化条件组合。
    /// </summary>
    private async Task<bool> EvaluateItemConditionAsync(
        JsonNode? data,
        int itemIndex,
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        // 主条件（表达式）
        if (!string.IsNullOrWhiteSpace(Condition.Source))
        {
            var mainResult = await Condition.EvaluateAsync<bool>(context, data, itemIndex, cancellationToken: cancellationToken);

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

        // 结构化条件
        if (Conditions.Count == 0)
        {
            return true;
        }

        var results = Conditions.Select(c => EvaluateStructuredCondition(c, data)).ToList();

        return Combinator == FilterCombinator.And
            ? results.All(r => r)
            : results.Any(r => r);
    }

    /// <summary>
    /// 结构化条件求值（LeftValue/Operation/RightValue）。
    /// </summary>
    private static bool EvaluateStructuredCondition(FilterCondition condition, JsonNode? data)
    {
        var leftValue = JsonPath.GetValue(data, condition.LeftValue);
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
