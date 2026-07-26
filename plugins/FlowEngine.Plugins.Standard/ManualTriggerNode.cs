using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using System.Text.Json.Nodes;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 手动触发器节点，用于手动测试工作流。
/// </summary>
[NodeMeta(TypeName = "manualTrigger", DisplayName = "Manual Trigger", Category = NodeCategory.Trigger, Icon = "play", DefaultIsEntry = true)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class ManualTriggerNode : NodeBase
{
    /// <inheritdoc />
    protected override AiNodeDefinition? GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "Manual Trigger", "Trigger", true,
            "人工手动触发工作流，是工作流的入口节点。常用于测试、调试或在 UI 中点击执行。",
            ["trigger", "manual"],
            null,
            AiDefinitionHelpers.Example("手动触发（无需参数）",
                JsonNode.Parse("{}"),
                JsonNode.Parse("""{"triggeredAt":"2026-07-12T09:00:00Z"}""")));

    /// <inheritdoc />
    public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        // Manual trigger just outputs an empty object
        return Task.FromResult(NodeHandlerOutput.Data(new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = new JsonObject
                    {
                        ["triggeredAt"] = DateTime.UtcNow.ToString("o")
                    },
                    Success = true,
                    SourceIndex = 0
                }
            ]
        }));
    }
}
