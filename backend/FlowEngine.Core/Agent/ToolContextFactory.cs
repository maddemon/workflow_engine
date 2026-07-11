using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.Agent;

/// <summary>
/// 工具上下文构造结果，包含执行上下文与节点类型实例。
/// </summary>
internal sealed record ToolContextResult(
    NodeExecutionContext Context,
    INodeType ToolNodeInstance);

/// <summary>
/// 工具上下文工厂，封装节点实例化与执行上下文构造逻辑。
/// </summary>
internal sealed class ToolContextFactory(
    NodeExecutionContext parentContext,
    ILogger? logger)
{
    /// <summary>
    /// 创建工具节点执行上下文。
    /// 先尝试通过 Activator 实例化节点，失败则回退到注册表中的节点类型实例。
    /// </summary>
    public async Task<ToolContextResult> CreateAsync(
        ToolResolution resolution,
        DataBatch inputBatch,
        DateTime startedAt,
        CancellationToken cancellationToken)
    {
        var nodeType = resolution.NodeType!;
        var toolNode = resolution.Node!;

        INodeType? toolNodeInstance;
        try
        {
            toolNodeInstance = (INodeType?)Activator.CreateInstance(nodeType.GetType());
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "创建工具节点实例失败，类型：{TypeName}。", nodeType.GetType().Name);
            toolNodeInstance = null;
        }

        toolNodeInstance ??= nodeType;

        NodeExecutionContext toolContext;
        if (parentContext.ContextFactory is not null && toolNodeInstance is not null)
        {
            var execution = new ExecutionRecord
            {
                Id = parentContext.ExecutionId,
                WorkflowDefinitionId = parentContext.Workflow.Id,
                ProjectId = parentContext.Workflow.ProjectId, // 冗余存储（GAP-11）
                StartedAt = startedAt,
                Status = ExecutionStatus.Running,
            };

            toolContext = await parentContext.ContextFactory.CreateAsync(
                parentContext.Workflow,
                execution,
                toolNode,
                toolNodeInstance,
                new Dictionary<string, DataBatch> { [FlowConstants.PortNames.Input] = inputBatch },
                new Dictionary<string, DataBatch>(),
                new Dictionary<string, DataBatch>(),
                0,
                cancellationToken).ConfigureAwait(false);
            // ContextFactory 不感知嵌套深度，需在此处显式递增（GAP-03）。
            toolContext.NestingDepth = parentContext.NestingDepth + 1;
        }
        else
        {
            // 降级/无工厂场景：父上下文未提供 ContextFactory（如测试或独立调用）。
            // 此时 RawParameters/ResolvedParameters 直接使用工具节点的原始参数，不做表达式求值与解析，
            // 与 if 分支（经完整 ParameterResolver/ScriptParameterPreEvaluator）语义不同，调用方需知悉此约束。
            toolContext = new NodeExecutionContext
            {
                Workflow = parentContext.Workflow,
                ExecutionId = parentContext.ExecutionId,
                Node = new NodeDefinition
                {
                    Id = toolNode.Id,
                    TypeName = toolNode.TypeName,
                    Name = toolNode.Name,
                    Parameters = toolNode.Parameters,
                    Ports = toolNode.Ports
                },
                Inputs = new Dictionary<string, DataBatch> { [FlowConstants.PortNames.Input] = inputBatch },
                RawParameters = toolNode.Parameters,
                ResolvedParameters = toolNode.Parameters,
                Credentials = parentContext.Credentials,
                Logger = parentContext.Logger,
                CancellationToken = cancellationToken,
                NestingDepth = parentContext.NestingDepth + 1
            };
        }

        return new ToolContextResult(toolContext, toolNodeInstance!);
    }
}
