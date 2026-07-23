using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 空操作节点：将输入原样传送到输出，用于调试、占位或分支布局。透传语义（OnceForAll）。
/// </summary>
public sealed class NoOpNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "noOp";

    /// <inheritdoc />
    public string DisplayName => "No Op";

    /// <inheritdoc />
    public string Category => "Flow";

    /// <inheritdoc />
    public string Icon => "noop";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // 透传：将整个输入批次原样输出（无输入时 GetInputBatch 返回空批次）。
            return context.Ok(context.GetInputBatch());
        }
        catch (Exception ex)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.UnexpectedError, ex.Message);
        }
    }

    /// <inheritdoc />
    AiNodeDefinition? INodeType.GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "No Op", "Flow", false,
            "空操作节点：将输入原样传送到输出，用于调试、占位或分支布局。",
            ["flow", "noop", "passthrough"],
            null,
            AiDefinitionHelpers.Example("透传",
                JsonNode.Parse("{}"),
                JsonNode.Parse("{}")));
}
