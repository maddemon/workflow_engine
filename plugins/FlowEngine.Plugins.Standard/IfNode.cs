using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Metadata;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 条件分支节点，根据条件表达式路由到 true 或 false 分支。
/// Condition 为 <see cref="Script"/> 类型，由管线在预求值阶段完成 Expression 求值并写入 <see cref="Script.ResolvedValue"/>，
/// 节点执行期经 <see cref="Script.GetResolved{T}"/> 读取强类型结果。新写法继承 <see cref="NodeBase"/>，
/// 通过 [NodeMeta]/[Port]/[Required]/[Hint] 声明式描述元信息与参数。
/// </summary>
[NodeMeta(TypeName = "if", DisplayName = "If", Category = NodeCategory.Flow, Icon = "shuffle")]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input)]
[Port(FlowConstants.PortNames.True, "True", PortDirection.Output)]
[Port(FlowConstants.PortNames.False, "False", PortDirection.Output)]
public sealed class IfNode : NodeBase
{
    /// <summary>
    /// 条件表达式，由管线预求值阶段完成求值并写入 <see cref="Script.ResolvedValue"/>。
    /// 支持 <c>$json.status === 'active'</c>、<c>$input.item().count > 10</c>、<c>$credentials.x.accessToken</c> 等
    /// 统一表达式变量模型中的所有 <c>$</c> 前缀变量。
    /// </summary>
    [Required]
    [Hint(PresentationHint.Expression)]
    [Description("Condition expression (e.g. $json.status === 'active', $input.item().count > 10).")]
    public Script Condition { get; set; } = Script.Empty;

    /// <inheritdoc />
    protected override AiNodeDefinition? GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "If", "Core", false,
            "条件分支节点。根据条件表达式把数据路由到 true 或 false 出口。条件表达式在引擎运行时求值，AI 只需在参数中提供条件。",
            ["logic", "branch", "condition"],
            null,
            AiDefinitionHelpers.Example("判断金额是否大于 100",
                JsonNode.Parse("""{"condition":"$input.first().amount > 100"}"""),
                null));

    /// <inheritdoc />
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        if (Condition is null || string.IsNullOrWhiteSpace(Condition.Source) || Condition.ResolvedValue is null)
        {
            throw new NodeExecutionException("MissingCondition", "Condition 参数不能为空或未被求值。");
        }

        var conditionResult = Condition.GetResolved<bool>();

        return conditionResult
            ? NodeHandlerOutput.ToPort(FlowConstants.PortNames.True, input.InputBatch)
            : NodeHandlerOutput.ToPort(FlowConstants.PortNames.False, input.InputBatch);
    }
}
