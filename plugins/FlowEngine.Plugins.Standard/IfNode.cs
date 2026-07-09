using FlowEngine.Core;
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
    /// 条件表达式，由 ParameterResolver 在工厂阶段求值。
    /// 支持 <c>$json.status === 'active'</c>、<c>$input.item().count > 10</c>、<c>$credentials.x.accessToken</c> 等
    /// 统一表达式变量模型中的所有 <c>$</c> 前缀变量。
    /// </summary>
    [DisplayName("Condition")]
    [Description("Condition expression (e.g. $json.status === 'active', $input.item().count > 10).")]
    [Hint(PresentationHint.Expression)]
    public string Condition { get; set; } = string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.True, DisplayName = "True", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.False, DisplayName = "False", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!context.ResolvedParameters.TryGetValue("condition", out var resolvedValue) || resolvedValue is null)
            {
                return Task.FromResult(context.ErrorResult("MissingCondition", "Condition 参数不能为空。"));
            }

            var conditionResult = ToBoolean(resolvedValue);

            var inputBatch = context.Inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch)
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

    /// <summary>
    /// 将 ParameterResolver 已求值的结果转换为布尔值。
    /// </summary>
    private static bool ToBoolean(object value)
    {
        if (value is bool b) return b;
        if (value is int i) return i != 0;
        if (value is long l) return l != 0;
        if (value is double d) return d != 0;
        if (value is string s)
        {
            if (bool.TryParse(s, out var boolResult)) return boolResult;
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return !string.IsNullOrEmpty(s);
        }
        return value is not null;
    }
}
