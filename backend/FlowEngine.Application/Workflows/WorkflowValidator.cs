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

        DeriveEntryNodes(workflow);
        ValidateDanglingConnections(workflow, errors);
        ValidatePortDirections(workflow, errors);
        ValidateOrphanNodes(workflow, errors);
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

            // 端口类型兼容性：AgentTool / LLM / Memory 端口只能连同类型端口
            if (sourcePort is not null && targetPort is not null
                && sourcePort.Type != PortType.Main && targetPort.Type != PortType.Main
                && sourcePort.Type != targetPort.Type)
            {
                errors.Add($"连接 {connection.Id} 的端口类型不兼容：源端口 '{connection.SourcePortName}' 为 {sourcePort.Type}，目标端口 '{connection.TargetPortName}' 为 {targetPort.Type}。");
            }
        }
    }

    /// <summary>
    /// 校验非入口节点必须至少有一条入边，防止孤立节点。
    /// 以下情况不视为孤立节点：
    /// - 节点本身是入口（<see cref="NodeDefinition.IsEntry"/>）；
    /// - 节点是触发器/可入口节点（无需入边即可启动工作流）；
    /// - 节点有出边（它是其它节点的来源，属于图的一部分）；
    /// - 工作流不存在任何连接（单节点草稿、断开操作后残留节点等，无图拓扑可言）。
    /// </summary>
    private void ValidateOrphanNodes(Workflow workflow, List<string> errors)
    {
        // 没有任何连接时，节点间不存在图关系，孤立检查无意义，直接跳过。
        if (workflow.Connections.Count == 0)
        {
            return;
        }

        var hasIncoming = workflow.Connections
            .Select(c => c.TargetNodeId)
            .ToHashSet();

        var hasOutgoing = workflow.Connections
            .Select(c => c.SourceNodeId)
            .ToHashSet();

        foreach (var node in workflow.Nodes)
        {
            if (node.IsEntry) continue;
            if (IsTriggerNode(node.TypeName)) continue;
            if (hasIncoming.Contains(node.Id)) continue;
            if (hasOutgoing.Contains(node.Id)) continue;

            errors.Add($"节点 '{node.Name}' ({node.TypeName}) 没有入边连接，工作流中不允许存在孤立节点。");
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
                // 带默认值的参数（如枚举）本质上是可选的，AI 不填时使用默认值即可，
                // 不应判为缺失（task-013 P5a）。
                if (parameter.DefaultValue is not null)
                {
                    continue;
                }

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

        var queue = new Queue<string>(inDegree.Where(x => x.Value == 0).Select(x => x.Key));
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

    /// <summary>
    /// 自动推导入口节点：将 Trigger 类型节点的 IsEntry 设为 true。
    /// 规则：
    /// - 仅有一个 Trigger 节点时，将其设为入口。
    /// - 有多个 Trigger 节点时，将第一个设为入口，其余保持原值。
    /// - 非 Trigger 节点即使显式设置 IsEntry=true 也保留。
    /// </summary>
    private void DeriveEntryNodes(Workflow workflow)
    {
        var triggerNodes = workflow.Nodes
            .Where(n => IsTriggerNode(n.TypeName))
            .ToList();

        if (triggerNodes.Count == 0)
        {
            return;
        }

        // 设计 §7.3：若已有 Trigger 显式声明 isEntry=true，尊重 AI 覆盖，不再强制第一个为入口。
        if (triggerNodes.Any(n => n.IsEntry))
        {
            return;
        }

        // 默认将第一个 Trigger 设为入口节点
        triggerNodes[0].IsEntry = true;
    }

    /// <summary>
    /// 判断节点类型是否为 Trigger 类型（Category == "Trigger" 或 DefaultIsEntry == true）。
    /// </summary>
    private bool IsTriggerNode(string typeName)
    {
        var descriptor = GetNodeDescriptor(typeName);
        return descriptor is not null &&
               (descriptor.Category.Equals("Trigger", StringComparison.OrdinalIgnoreCase) || descriptor.DefaultIsEntry);
    }

    /// <summary>
    /// 验证工作流至少包含一个 Trigger 节点。
    /// </summary>
    /// <summary>
    /// 校验工作流是否包含至少一个触发器节点。
    /// 此校验仅适用于 AI 组装/激活场景，不包含在 <see cref="Validate"/> 中。
    /// </summary>
    public void ValidateTriggerNodes(Workflow workflow, List<string> errors)
    {
        if (!workflow.Nodes.Any(n => IsTriggerNode(n.TypeName)))
        {
            errors.Add("工作流必须至少包含一个触发器节点。");
        }
    }

    private NodeTypeDescriptor? GetNodeDescriptor(string typeName)
    {
        return registry.GetDescriptors()
            .FirstOrDefault(d => d.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase));
    }
}
