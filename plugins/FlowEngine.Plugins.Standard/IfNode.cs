using FlowEngine.Core;
using FlowEngine.Core.Ai;
using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 条件分支节点，根据条件表达式路由到 true 或 false 分支。
/// Condition 为 <see cref="Script"/> 类型，由工厂在预求值阶段完成 Expression 求值并写入 ResolvedValue。
/// </summary>
public sealed class IfNode : INodeType, IAiDefinitionProvider
{
    /// <inheritdoc />
    public string TypeName => "if";

    /// <inheritdoc />
    public AiNodeDefinition GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "If", "Core", false,
            "条件分支节点。根据条件表达式把数据路由到 true 或 false 出口。条件表达式在引擎运行时求值，AI 只需在参数中提供条件。",
            ["logic", "branch", "condition"],
            null,
            AiDefinitionHelpers.Example("判断金额是否大于 100",
                JsonNode.Parse("""{"condition":"$input.first().amount > 100"}"""),
                null));

    /// <inheritdoc />
    public string DisplayName => "If";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "shuffle";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 条件表达式，由工厂在预求值阶段完成求值。
    /// 支持 <c>$json.status === 'active'</c>、<c>$input.item().count > 10</c>、<c>$credentials.x.accessToken</c> 等
    /// 统一表达式变量模型中的所有 <c>$</c> 前缀变量。
    /// </summary>
    [DisplayName("Condition")]
    [Description("Condition expression (e.g. $json.status === 'active', $input.item().count > 10).")]
    [Hint(PresentationHint.Expression)]
    public Script Condition { get; set; } = Script.Empty;

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
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (Condition is null || string.IsNullOrWhiteSpace(Condition.Source))
            {
                return context.ErrorResult("MissingCondition", "Condition 参数不能为空。");
            }

            var conditionResult = await Condition.EvaluateAsync<bool>(context, cancellationToken: cancellationToken);

            var inputBatch = context.GetInputBatch();

            return new NodeExecutionResult
            {
                Success = true,
                Output = inputBatch,
                BranchIndex = conditionResult ? 0 : 1
            };
        }
        catch (Exception ex)
        {
            return context.ErrorResult("ConditionError", $"条件求值失败: {ex.Message}");
        }
    }
}
