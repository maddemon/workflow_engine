using System.Diagnostics.CodeAnalysis;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Entities;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// AI 节点目录服务，提供节点发现能力。
/// </summary>
public sealed class CatalogService(INodeRegistry nodeRegistry)
{
    /// <summary>
    /// 获取所有节点的 AI 摘要列表。
    /// </summary>
    /// <returns>节点摘要列表。</returns>
    public IReadOnlyList<AiNodeSummary> ListAll()
    {
        var descriptors = nodeRegistry.GetDescriptors();
        var nodeTypes = nodeRegistry.GetAll();

        var results = new List<AiNodeSummary>(descriptors.Count);

        foreach (var descriptor in descriptors)
        {
            var nodeType = nodeTypes.FirstOrDefault(n =>
                n.TypeName.Equals(descriptor.TypeName, StringComparison.OrdinalIgnoreCase));

            if (nodeType is null)
            {
                continue;
            }

            var definition = NodeDefinitionAdapter.ToAiDefinition(nodeType, descriptor);
            results.Add(NodeDefinitionAdapter.ToSummary(definition));
        }

        return results;
    }

    /// <summary>
    /// 按类型名获取节点完整定义。
    /// </summary>
    /// <param name="name">节点类型名。</param>
    /// <returns>AI 节点定义，未找到时返回 null。</returns>
    public AiNodeDefinition? GetByName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (!nodeRegistry.TryGet(name, out var nodeType) || nodeType is null)
        {
            return null;
        }

        NodeTypeDescriptor descriptor;
        try
        {
            descriptor = nodeRegistry.GetDescriptor(name);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return NodeDefinitionAdapter.ToAiDefinition(nodeType, descriptor);
    }
}
