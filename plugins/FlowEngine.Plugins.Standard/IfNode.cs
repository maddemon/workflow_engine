using System.ComponentModel;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 条件分支节点，根据条件表达式路由到 true 或 false 分支。
/// 条件值由执行引擎的 ParameterResolver 预先求值后传入 ResolvedParameters。
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
    /// 条件表达式，求值结果应为 true 或 false。
    /// 支持比较运算符：==, !=, >, <, >=, <=
    /// 示例：<c>input.status == "active"</c> 或 <c>input.count > 10</c>
    /// </summary>
    [DisplayName("Condition")]
    [Description("Condition that evaluates to true or false. Supports ==, !=, >, <, >=, <= operators (e.g. input.status == 'active').")]
    [Hint("language", ScriptLanguage.JavaScript)]
    public string Condition { get; set; } = string.Empty;

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
            if (string.IsNullOrWhiteSpace(Condition))
            {
                return Task.FromResult(context.ErrorResult("MissingCondition", "Condition 参数不能为空。"));
            }

            var conditionResult = ToBoolean(Condition);

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
            return Task.FromResult(context.ErrorResult("ConditionError", $"条件求值失败: {ex.Message}"));
        }
    }

    private static bool ToBoolean(string value)
    {
        var trimmed = value.Trim();

        var operators = new[] { ">=", "<=", "==", "!=", ">", "<" };
        foreach (var op in operators)
        {
            var idx = trimmed.IndexOf(op, StringComparison.Ordinal);
            if (idx > 0 && idx < trimmed.Length - op.Length)
            {
                var leftExpr = trimmed[..idx].Trim();
                var rightExpr = trimmed[(idx + op.Length)..].Trim();
                return Compare(leftExpr, rightExpr, op);
            }
        }

        if (bool.TryParse(trimmed, out var b)) return b;
        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;

        if (double.TryParse(trimmed, out var n)) return n != 0;

        return !string.IsNullOrEmpty(trimmed);
    }

    private static bool Compare(string left, string right, string op)
    {
        left = Unquote(left);
        right = Unquote(right);

        if (double.TryParse(left, out var leftNum) && double.TryParse(right, out var rightNum))
        {
            return op switch
            {
                ">=" => leftNum >= rightNum,
                "<=" => leftNum <= rightNum,
                "==" => leftNum == rightNum,
                "!=" => leftNum != rightNum,
                ">" => leftNum > rightNum,
                "<" => leftNum < rightNum,
                _ => false
            };
        }

        return op switch
        {
            "==" => string.Equals(left, right, StringComparison.Ordinal),
            "!=" => !string.Equals(left, right, StringComparison.Ordinal),
            ">=" => string.Compare(left, right, StringComparison.Ordinal) >= 0,
            "<=" => string.Compare(left, right, StringComparison.Ordinal) <= 0,
            ">" => string.Compare(left, right, StringComparison.Ordinal) > 0,
            "<" => string.Compare(left, right, StringComparison.Ordinal) < 0,
            _ => false
        };
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if ((trimmed.StartsWith('\'') && trimmed.EndsWith('\'')) ||
            (trimmed.StartsWith('"') && trimmed.EndsWith('"')))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }
}
