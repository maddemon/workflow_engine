using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Tools;

namespace FlowEngine.Infrastructure.Services;

/// <summary>
/// 子执行服务实现：包装 <see cref="INodeExecutionContextFactory"/> 构造上下文并执行单次子节点，
/// 作为节点发起子执行（如子工作流、递归调用）的统一抽象。
/// </summary>
public sealed class SubExecutionService(INodeExecutionContextFactory factory, INodeRegistry nodeRegistry) : ISubExecutionService
{
    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteSubAsync(
        Workflow workflow,
        ExecutionRecord execution,
        NodeDefinition node,
        INodeType nodeType,
        IReadOnlyDictionary<string, DataBatch> inputs,
        int runIndex,
        CancellationToken ct = default)
    {
        var context = await factory.CreateAsync(
            workflow,
            execution,
            node,
            nodeType,
            inputs,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            runIndex,
            ct).ConfigureAwait(false);

        return await nodeType.ExecuteAsync(context, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ToolDefinition>> ResolveAgentToolsAsync(NodeExecutionContext context, CancellationToken ct = default)
    {
        var toolConnections = context.Workflow.Connections
            .Where(c => c.TargetNodeId == context.Node.Id && c.TargetPortName == FlowConstants.PortNames.Tools)
            .ToList();

        if (toolConnections.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ToolDefinition>>([]);
        }

        var tools = new List<ToolDefinition>();
        foreach (var connection in toolConnections)
        {
            var toolNode = context.Workflow.Nodes.FirstOrDefault(n => n.Id == connection.SourceNodeId);
            if (toolNode is null)
            {
                continue;
            }

            if (!nodeRegistry.TryGet(toolNode.TypeName, out var nodeType) || nodeType is null)
            {
                continue;
            }

            NodeTypeDescriptor? descriptor = null;
            try
            {
                descriptor = nodeRegistry.GetDescriptor(toolNode.TypeName);
            }
            catch (InvalidOperationException)
            {
                // Descriptor not found, skip
            }

            var parametersSchema = SchemaDerivation.DeriveSchema(descriptor?.Parameters);

            tools.Add(new ToolDefinition
            {
                Name = toolNode.Name,
                Description = ResolveToolDescription(nodeType, descriptor),
                TargetNodeDefinitionId = toolNode.Id,
                ParametersSchema = parametersSchema
            });
        }

        return Task.FromResult<IReadOnlyList<ToolDefinition>>(tools);
    }

    /// <summary>
    /// 解析工具描述，优先使用参数中的 AI 参数占位符描述，回退到节点 DisplayName。
    /// 与插件内 <c>AgentToolDescriptionHelper.ResolveToolDescription</c> 语义一致（基础设施层无法引用插件内部类型，故内联实现）。
    /// </summary>
    private static string ResolveToolDescription(INodeType nodeType, NodeTypeDescriptor? descriptor)
    {
        var description = nodeType.DisplayName;
        if (descriptor?.Parameters is { Count: > 0 })
        {
            var aiParam = descriptor.Parameters.FirstOrDefault(p => SchemaDerivation.HasAiParamPlaceholder(p.Description));
            if (aiParam?.Description is not null)
            {
                description = SchemaDerivation.ResolveAiParamDescription(aiParam.Description) ?? description;
            }
        }

        return description;
    }
}
