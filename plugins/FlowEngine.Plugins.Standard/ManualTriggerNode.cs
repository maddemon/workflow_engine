using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 手动触发器节点，用于手动测试工作流。
/// </summary>
public sealed class ManualTriggerNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "manualTrigger";

    /// <inheritdoc />
    public string DisplayName => "Manual Trigger";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "play";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => true;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        // Manual trigger just outputs an empty object
        return Task.FromResult(context.Ok(new System.Text.Json.Nodes.JsonObject
        {
            ["triggeredAt"] = DateTime.UtcNow.ToString("o")
        }));
    }
}
