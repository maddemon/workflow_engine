using System.ComponentModel;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// Switch 分支节点，根据表达式值路由到不同的输出端口。
/// 每个 Case 对应一个输出端口，不匹配时路由到 default 端口。
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
    public ExecutionMode ExecutionMode => ExecutionMode.OncePerItem;

    /// <summary>
    /// 表达式（如 <c>{{ input.category }}</c>），求值结果与 Case 的 Value 匹配。
    /// </summary>
    [Description("Expression to evaluate (e.g. {{ input.category }}).")]
    public string Expression { get; set; } = string.Empty;

    /// <summary>
    /// Case 列表，每个 Case 路由到一个独立的输出端口。
    /// </summary>
    [Item(typeof(SwitchCase))]
    [Description("Case list. Each case routes to a separate output port.")]
    public List<SwitchCase> Cases { get; set; } = [];

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports =>
    [
        new PortDefinition { Name = "input", DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        .. Cases.Select(c => new PortDefinition
        {
            Name = c.Name,
            DisplayName = c.Label,
            Direction = PortDirection.Output,
            Type = PortType.Main
        }),
        new PortDefinition { Name = "default", DisplayName = "Default", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var match = Cases.FindIndex(c =>
            string.Equals(c.Value, Expression, StringComparison.OrdinalIgnoreCase));

        var inputBatch = context.Inputs.Values.FirstOrDefault() ?? new DataBatch();

        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = inputBatch,
            BranchIndex = match >= 0 ? match : Cases.Count
        });
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
