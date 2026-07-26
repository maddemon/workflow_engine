using System.ComponentModel;
using System.Linq;
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
/// Switch 分支节点，根据表达式值路由到不同的输出端口。
/// 每个 Case 对应一个独立的动态输出端口（命名 case{i}）；不匹配时路由到 _default 端口。
/// 新写法继承 <see cref="NodeBase"/>，动态端口经 <see cref="GetExtraPorts"/> 在参数水合后生成，
/// 路由值经管线在预求值阶段写入 <see cref="Script.ResolvedValue"/>，执行期用 <see cref="Script.GetResolved{T}"/> 读取。
/// </summary>
[NodeMeta(TypeName = "switch", DisplayName = "Switch", Category = NodeCategory.Flow, Icon = "git-branch")]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input)]
[Port(FlowConstants.PortNames.Default, "Default", PortDirection.Output)]
public sealed class SwitchNode : NodeBase
{
    /// <summary>
    /// 要匹配的值表达式，支持 JS 表达式（如 <c>$json.category</c> 或 <c>"admin"</c>）。
    /// 须使用 <see cref="PresentationHint.Expression"/> 提示，管线方会在预求值阶段完成求值并写入
    /// <see cref="Script.ResolvedValue"/>（见 <c>ScriptParameterPreEvaluatorCore</c>）；执行期经
    /// <see cref="Script.GetResolved{T}"/> 读取。若为 <see cref="PresentationHint.Script"/>，则不会被预求值，
    /// 导致 <see cref="Script.ResolvedValue"/> 为空而抛 <see cref="NodeParameterException"/>。
    /// </summary>
    [Required]
    [Hint(PresentationHint.Expression)]
    [Description("Value to match against cases. Use JS expression to access input data (e.g. $json.category).")]
    public Script Expression { get; set; } = Script.Empty;

    /// <summary>
    /// Case 列表，每个 Case 路由到一个独立的输出端口。
    /// </summary>
    [Hint(PresentationHint.Array, "itemType", typeof(SwitchCase))]
    [Description("Case list. Each case routes to a separate output port.")]
    public List<SwitchCase> Cases { get; set; } = [];

    /// <inheritdoc />
    protected override AiNodeDefinition? GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "Switch", "Core", false,
            "多路分支节点。按多个条件把数据路由到匹配的出口（如 case1/case2/.../default）。条件在引擎运行时求值。",
            ["logic", "branch", "switch"],
            null,
            AiDefinitionHelpers.Example("按状态分流",
                JsonNode.Parse("""{"cases":[{"label":"ok","value":"ok"}]}"""),
                null));

    /// <inheritdoc />
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        var value = Expression.GetResolved<string>();

        var matchIndex = Cases.FindIndex(c =>
            string.Equals(c.Value, value, StringComparison.OrdinalIgnoreCase));

        return matchIndex >= 0
            ? NodeHandlerOutput.ToPort($"case{matchIndex}", input.InputBatch)
            : NodeHandlerOutput.ToPort(FlowConstants.PortNames.Default, input.InputBatch);
    }

    /// <inheritdoc />
    /// <summary>运行时动态端口：为 <see cref="Cases"/> 中的每个条目生成一个输出端口（命名 case{i}），
    /// 叠加在基类端口（input + _default）之上。须在参数水合后于真实实例上调用（见计划 §A.4.1）。</summary>
    protected override IReadOnlyList<PortDefinition> GetExtraPorts()
    {
        return Cases.Select((c, i) => new PortDefinition
        {
            Name = $"case{i}",
            DisplayName = c.DisplayName ?? c.Label ?? c.Value,
            Direction = PortDirection.Output,
            Type = PortType.Main
        }).ToList();
    }
}

/// <summary>
/// Switch 节点的 Case 定义。
/// </summary>
public sealed class SwitchCase
{
    /// <summary>
    /// 端口名称（唯一标识）。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 显示标签（兼容旧工作流 JSON 的 label 字段）。
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// 显示名（优先于 <see cref="Label"/>；旧数据缺省时回退到 Label）。
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 匹配值。
    /// </summary>
    public string Value { get; set; } = string.Empty;
}
