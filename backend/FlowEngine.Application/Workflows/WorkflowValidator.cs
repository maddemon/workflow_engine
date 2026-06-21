using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流保存校验器。
/// </summary>
/// <remarks>
/// 初始化校验器。
/// </remarks>
/// <param name="registry">节点注册中心。</param>
public sealed class WorkflowValidator(INodeRegistry registry)
{

    /// <summary>
    /// 校验工作流是否可保存。
    /// </summary>
    /// <param name="workflow">工作流实例。</param>
    /// <returns>校验结果。</returns>
    public ValidationResult Validate(Workflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var errors = new List<string>();

        ValidateDanglingConnections(workflow, errors);
        ValidatePortDirections(workflow, errors);
        ValidateRequiredParameters(workflow, errors);
        ValidateCycles(workflow, errors);

        return new ValidationResult(errors);
    }

    private static void ValidateDanglingConnections(Workflow workflow, List<string> errors)
    {
        var nodeIds = workflow.Nodes.Select(n => n.Id).ToHashSet();

        foreach (var connection in workflow.Connections)
        {
            if (!nodeIds.Contains(connection.SourceNodeId))
            {
                errors.Add($"连接 {connection.Id} 的源节点不存在。");
            }

            if (!nodeIds.Contains(connection.TargetNodeId))
            {
                errors.Add($"连接 {connection.Id} 的目标节点不存在。");
            }
        }
    }

    private void ValidatePortDirections(Workflow workflow, List<string> errors)
    {
        var nodeMap = workflow.Nodes.ToDictionary(n => n.Id);

        foreach (var connection in workflow.Connections)
        {
            if (!nodeMap.TryGetValue(connection.SourceNodeId, out var sourceNode))
            {
                continue;
            }

            if (!nodeMap.TryGetValue(connection.TargetNodeId, out var targetNode))
            {
                continue;
            }

            var sourceDescriptor = GetNodeDescriptor(sourceNode.TypeName);
            var targetDescriptor = GetNodeDescriptor(targetNode.TypeName);

            var sourcePort = sourceDescriptor?.Ports
                .FirstOrDefault(p => p.Name.Equals(connection.SourcePortName, StringComparison.OrdinalIgnoreCase));

            var targetPort = targetDescriptor?.Ports
                .FirstOrDefault(p => p.Name.Equals(connection.TargetPortName, StringComparison.OrdinalIgnoreCase));

            if (sourcePort is not null && sourcePort.Direction != PortDirection.Output)
            {
                errors.Add($"连接 {connection.Id} 的源端口 '{connection.SourcePortName}' 不是输出端口。");
            }

            if (targetPort is not null && targetPort.Direction != PortDirection.Input)
            {
                errors.Add($"连接 {connection.Id} 的目标端口 '{connection.TargetPortName}' 不是输入端口。");
            }
        }
    }

    private void ValidateRequiredParameters(Workflow workflow, List<string> errors)
    {
        foreach (var node in workflow.Nodes)
        {
            var descriptor = GetNodeDescriptor(node.TypeName);
            if (descriptor is null)
            {
                continue;
            }

            foreach (var parameter in descriptor.Parameters.Where(p => p.Required))
            {
                if (!node.Parameters.TryGetValue(parameter.Name, out var value) || value is null)
                {
                    errors.Add($"节点 '{node.Name}' 缺少必填参数 '{parameter.DisplayName}'。");
                }
            }
        }
    }

    private static void ValidateCycles(Workflow workflow, List<string> errors)
    {
        var nodeIds = workflow.Nodes.Select(n => n.Id).ToHashSet();
        var adjacency = workflow.Nodes.ToDictionary(
            n => n.Id,
            n => workflow.Connections
                .Where(c => c.SourceNodeId == n.Id && nodeIds.Contains(c.TargetNodeId))
                .Select(c => c.TargetNodeId)
                .ToList());

        var inDegree = workflow.Nodes.ToDictionary(n => n.Id, _ => 0);
        foreach (var targets in adjacency.Values)
        {
            foreach (var target in targets)
            {
                inDegree[target]++;
            }
        }

        var queue = new Queue<Guid>(inDegree.Where(x => x.Value == 0).Select(x => x.Key));
        var visited = 0;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            visited++;

            foreach (var next in adjacency[current])
            {
                inDegree[next]--;
                if (inDegree[next] == 0)
                {
                    queue.Enqueue(next);
                }
            }
        }

        if (visited != workflow.Nodes.Count)
        {
            errors.Add("工作流存在循环依赖。");
        }
    }

    private NodeTypeDescriptor? GetNodeDescriptor(string typeName)
    {
        return registry.GetDescriptors()
            .FirstOrDefault(d => d.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase));
    }
}
