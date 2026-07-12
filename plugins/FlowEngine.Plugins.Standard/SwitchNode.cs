using FlowEngine.Core;
using FlowEngine.Core.Ai;
using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// Switch 分支节点，根据表达式值路由到不同的输出端口。
/// 每个 Case 对应一个输出端口，不匹配时路由到 default 端口。
/// </summary>
public sealed class SwitchNode : INodeType, IAiDefinitionProvider
{
    /// <inheritdoc />
    public string TypeName => "switch";

    /// <inheritdoc />
    public AiNodeDefinition GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "Switch", "Core", false,
            "多路分支节点。按多个条件把数据路由到匹配的出口（如 case1/case2/.../default）。条件在引擎运行时求值。",
            ["logic", "branch", "switch"],
            null,
            AiDefinitionHelpers.Example("按状态分流",
                JsonNode.Parse("""{"cases":[{"label":"ok","condition":"$input.first().status == 'ok'"}]}"""),
                null));

    /// <inheritdoc />
    public string DisplayName => "Switch";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "git-branch";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OncePerItem;

    /// <summary>
    /// 要匹配的值，支持 JS 表达式（如 <c>$json.category</c> 或 <c>"admin"</c>）。
    /// </summary>
    [DisplayName("Value")]
    [Description("Value to match against cases. Use JS expression to access input data (e.g. $json.category).")]
    [Hint(PresentationHint.Expression)]
    public Script Expression { get; set; } = Script.Empty;

    /// <summary>
    /// Case 列表，每个 Case 路由到一个独立的输出端口。
    /// </summary>
    [Hint(PresentationHint.Array, "itemType", typeof(SwitchCase))]
    [Description("Case list. Each case routes to a separate output port.")]
    public List<SwitchCase> Cases { get; set; } = [];

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports =>
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        .. Cases.Select(c => new PortDefinition
        {
            Name = c.Name,
            DisplayName = c.Label,
            Direction = PortDirection.Output,
            Type = PortType.Main
        }),
        new PortDefinition { Name = FlowConstants.PortNames.Default, DisplayName = "Default", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await Expression.EvaluateAsync<string>(context, cancellationToken: cancellationToken);

            var match = Cases.FindIndex(c =>
                string.Equals(c.Value, value, StringComparison.OrdinalIgnoreCase));

            var inputBatch = context.Inputs.Values.FirstOrDefault() ?? new DataBatch();

            return new NodeExecutionResult
            {
                Success = true,
                Output = inputBatch,
                BranchIndex = match >= 0 ? match : Cases.Count
            };
        }
        catch (Exception ex)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected switch error: {ex.Message}");
        }
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
    /// 显示标签。
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// 匹配值。
    /// </summary>
    public string Value { get; set; } = string.Empty;
}
