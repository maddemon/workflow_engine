using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Agent;

/// <summary>
/// 工具解析结果，包含工具定义、节点定义、节点类型实例及可能的错误信息。
/// </summary>
internal sealed record ToolResolution(
    ToolDefinition? Tool,
    NodeDefinition? Node,
    INodeType? NodeType,
    string? Error)
{
    public bool HasError => Error is not null;
}

/// <summary>
/// 工具解析器，封装工具查找、节点查找、节点类型查找逻辑。
/// </summary>
internal sealed class ToolResolver(
    IReadOnlyList<ToolDefinition> tools,
    NodeExecutionContext parentContext)
{
    /// <summary>
    /// 解析工具调用所需的三级查找：工具定义 → 节点定义 → 节点类型。
    /// </summary>
    public ToolResolution Resolve(LlmToolCall toolCall)
    {
        var tool = tools.FirstOrDefault(t => t.Name == toolCall.Name);
        if (tool is null)
        {
            return new ToolResolution(null, null, null, $"Tool '{toolCall.Name}' not found.");
        }

        var toolNode = parentContext.Workflow.Nodes
            .FirstOrDefault(n => n.Id == tool.TargetNodeDefinitionId);
        if (toolNode is null)
        {
            return new ToolResolution(tool, null, null, $"Tool node '{tool.TargetNodeDefinitionId}' not found.");
        }

        if (parentContext.NodeRegistry?.TryGet(toolNode.TypeName, out var nodeType) != true
            || nodeType is null)
        {
            return new ToolResolution(tool, toolNode, null, $"Node type '{toolNode.TypeName}' not found.");
        }

        return new ToolResolution(tool, toolNode, nodeType, null);
    }
}
