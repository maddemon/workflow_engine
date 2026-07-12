using FlowEngine.Core;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using System.Text.Json.Nodes;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 手动触发器节点，用于手动测试工作流。
/// </summary>
public sealed class ManualTriggerNode : INodeType, IAiDefinitionProvider
{
    /// <inheritdoc />
    public string TypeName => "manualTrigger";

    /// <inheritdoc />
    public AiNodeDefinition GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "Manual Trigger", "Trigger", true,
            "人工手动触发工作流，是工作流的入口节点。常用于测试、调试或在 UI 中点击执行。",
            ["trigger", "manual"],
            null,
            AiDefinitionHelpers.Example("手动触发（无需参数）",
                JsonNode.Parse("{}"),
                JsonNode.Parse("""{"triggeredAt":"2026-07-12T09:00:00Z"}""")));

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
