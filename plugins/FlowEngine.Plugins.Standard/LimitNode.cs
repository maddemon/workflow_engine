using System.ComponentModel;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 限制节点，限制数据项数量。
/// </summary>
public sealed class LimitNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "limit";

    /// <inheritdoc />
    public string DisplayName => "Limit";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "hash";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 最大项目数。
    /// </summary>
    [Description("Maximum number of items to output.")]
    public int MaxItems { get; set; } = 10;

    /// <summary>
    /// 跳过的项目数。
    /// </summary>
    [Description("Number of items to skip from the beginning.")]
    public int Skip { get; set; } = 0;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = "input", DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = "output", DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var inputBatch = context.Inputs.TryGetValue("input", out var batch)
            ? batch
            : new DataBatch();

        var items = inputBatch.Items
            .Skip(Skip)
            .Take(MaxItems)
            .ToList();

        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = items }
        });
    }
}
