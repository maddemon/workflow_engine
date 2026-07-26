using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 空操作节点：将输入原样传送到输出，用于调试、占位或分支布局。透传语义（OnceForAll）。
/// </summary>
[NodeMeta(TypeName = "noOp", DisplayName = "No Op", Category = NodeCategory.Flow, Icon = "noop", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class NoOpNode : NodeBase
{
    /// <inheritdoc />
    public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        try
        {
            // 透传：将整个输入批次原样输出（无输入时 InputBatch 返回空批次）。
            return Task.FromResult(NodeHandlerOutput.Data(input.InputBatch));
        }
        catch (Exception ex)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.UnexpectedError, ex.Message);
        }
    }

    /// <inheritdoc />
    protected override AiNodeDefinition? GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "No Op", "Flow", false,
            "空操作节点：将输入原样传送到输出，用于调试、占位或分支布局。",
            ["flow", "noop", "passthrough"],
            null,
            AiDefinitionHelpers.Example("透传",
                JsonNode.Parse("{}"),
                JsonNode.Parse("{}")));
}
