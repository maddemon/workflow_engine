using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// Switch 分支节点，根据表达式值路由到不同的输出端口。
/// 支持 Rules 模式（按值匹配）和 Expression 模式（按表达式值）。
/// </summary>
public sealed class SwitchNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "switch";

    /// <inheritdoc />
    public string DisplayName => "Switch";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "git-branch";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 路由模式。
    /// </summary>
    [Description("How to route items.")]
    public SwitchMode Mode { get; set; } = SwitchMode.Rules;

    /// <summary>
    /// 表达式（Expression 模式下使用）。
    /// </summary>
    [Description("Expression to evaluate for routing (e.g. {{ $json.category }}).")]
    public string Expression { get; set; } = string.Empty;

    /// <summary>
    /// 输出数量（Expression 模式下使用）。
    /// </summary>
    [Description("Number of outputs for expression mode.")]
    public int NumberOutputs { get; set; } = 2;

    /// <summary>
    /// 规则列表（Rules 模式下使用）。
    /// </summary>
    [Description("Rules for routing items. Each rule creates an output.")]
    public List<SwitchRule> Rules { get; set; } = [];

    /// <summary>
    /// 是否包含兜底输出。
    /// </summary>
    [Description("Whether to include a fallback output for unmatched items.")]
    public bool IncludeFallback { get; set; } = true;

    /// <summary>
    /// 兜底输出名称。
    /// </summary>
    [Description("Display name for the fallback output.")]
    public string FallbackName { get; set; } = "Fallback";

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports =>
    [
        new PortDefinition { Name = "input", DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        .. GetOutputPorts()
    ];

    private IEnumerable<PortDefinition> GetOutputPorts()
    {
        if (Mode == SwitchMode.Expression)
        {
            for (var i = 0; i < NumberOutputs; i++)
            {
                yield return new PortDefinition
                {
                    Name = $"output_{i}",
                    DisplayName = i.ToString(),
                    Direction = PortDirection.Output,
                    Type = PortType.Main
                };
            }
        }
        else
        {
            foreach (var rule in Rules)
            {
                yield return new PortDefinition
                {
                    Name = rule.Name,
                    DisplayName = rule.Label,
                    Direction = PortDirection.Output,
                    Type = PortType.Main
                };
            }
        }

        if (IncludeFallback)
        {
            yield return new PortDefinition
            {
                Name = "fallback",
                DisplayName = FallbackName,
                Direction = PortDirection.Output,
                Type = PortType.Main
            };
        }
    }

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var inputBatch = context.Inputs.Values.FirstOrDefault() ?? new DataBatch();

        var branchIndex = Mode == SwitchMode.Expression
            ? EvaluateExpression(context)
            : EvaluateRules(context);

        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = inputBatch,
            BranchIndex = branchIndex
        });
    }

    private int EvaluateExpression(NodeExecutionContext context)
    {
        var value = ResolveExpression(Expression, context);
        if (int.TryParse(value, out var index) && index >= 0 && index < NumberOutputs)
        {
            return index;
        }

        // Try to find matching output by value
        for (var i = 0; i < NumberOutputs; i++)
        {
            if (string.Equals(i.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return IncludeFallback ? NumberOutputs : 0;
    }

    private int EvaluateRules(NodeExecutionContext context)
    {
        var value = ResolveExpression(Expression, context);

        for (var i = 0; i < Rules.Count; i++)
        {
            var rule = Rules[i];
            if (string.Equals(rule.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return IncludeFallback ? Rules.Count : 0;
    }

    private string? ResolveExpression(string expression, NodeExecutionContext context)
    {
        if (string.IsNullOrEmpty(expression))
        {
            return null;
        }

        // Use Jint to evaluate JS expression
        try
        {
            var inputData = context.InputData;
            using var js = FlowEngine.Runtime.Scripting.JsEngine.Create();

            js.SetValue("input", inputData);
            var result = js.Evaluate(expression);
            return result?.ToString();
        }
        catch
        {
            // If Jint fails, return the raw value
            return expression;
        }
    }
}

/// <summary>
/// Switch 节点的路由模式。
/// </summary>
public enum SwitchMode
{
    /// <summary>按规则匹配</summary>
    Rules,

    /// <summary>按表达式值</summary>
    Expression
}

/// <summary>
/// Switch 节点的规则定义。
/// </summary>
public sealed class SwitchRule
{
    /// <summary>
    /// 端口名称（唯一标识）。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 显示标签。
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// 匹配值。
    /// </summary>
    public string Value { get; set; } = string.Empty;
}
