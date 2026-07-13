using FlowEngine.Application.Dtos;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流装配服务——将 AI 草稿 DSL 转换为完整的、可持久化的工作流定义。
/// </summary>
public sealed class WorkflowAssemblyService(
    INodeRegistry nodeRegistry,
    WorkflowService workflowService,
    WorkflowValidator workflowValidator)
    : IWorkflowAssemblyService
{
    /// <summary>
    /// 装配 AI 草稿为完整工作流并创建草稿记录。
    /// </summary>
    /// <param name="request">AI 装配请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>装配结果。</returns>
    /// <exception cref="BusinessException">节点类型未注册、节点 ID 重复等结构性问题时抛出。</exception>
    public async Task<AssembleWorkflowResult> AssembleAsync(
        AssembleWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ── 1. 解析并补全节点 ID，校验唯一性 ──────────────────────
        // 设计 §5.2 步骤 1：id 缺失时按 typeName 自动生成，并保证工作流内唯一。
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var resolvedNodes = new List<(AiDraftNodeDto Node, string Id)>(request.Nodes.Count);
        foreach (var draftNode in request.Nodes)
        {
            var id = string.IsNullOrWhiteSpace(draftNode.Id)
                ? GenerateNodeId(draftNode.TypeName, usedIds)
                : draftNode.Id;

            if (!usedIds.Add(id))
            {
                throw new BusinessException($"节点 ID 重复: {id}");
            }

            resolvedNodes.Add((draftNode, id));
        }

        // ── 2. 解析每个节点 ────────────────────────────────────
        var nodes = new List<NodeDefinition>(request.Nodes.Count);
        foreach (var (draftNode, resolvedId) in resolvedNodes)
        {
            if (string.IsNullOrWhiteSpace(draftNode.TypeName))
            {
                throw new BusinessException($"节点 '{resolvedId}' 的 TypeName 不能为空。");
            }

            // 查找节点类型
            NodeTypeDescriptor descriptor;
            try
            {
                descriptor = nodeRegistry.GetDescriptor(draftNode.TypeName);
            }
            catch (InvalidOperationException)
            {
                throw new BusinessException(
                    $"节点 '{resolvedId}' 使用了未知的节点类型 '{draftNode.TypeName}'。");
            }

            // 从端口定义创建端口实例
            var ports = descriptor.Ports.Select(p => new PortInstance
            {
                Name = p.Name,
                Direction = p.Direction,
                Type = p.Type,
            }).ToList();

            var node = new NodeDefinition
            {
                Id = resolvedId,
                TypeName = draftNode.TypeName,
                Name = resolvedId, // 默认以 AI 赋予的 ID 作为显示名称
                Parameters = draftNode.Parameters,
                Ports = ports,
                PositionX = null,
                PositionY = null,
                IsEntry = false,
            };

            nodes.Add(node);
        }

        var nodeIds = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

        // ── 3. 解析连接 ────────────────────────────────────────
        var connections = new List<Connection>(request.Connections.Count);
        foreach (var draftConn in request.Connections)
        {
            if (string.IsNullOrWhiteSpace(draftConn.From))
            {
                throw new BusinessException("连接缺少源节点 ID (From)。");
            }

            if (string.IsNullOrWhiteSpace(draftConn.To))
            {
                throw new BusinessException("连接缺少目标节点 ID (To)。");
            }

            if (!nodeIds.Contains(draftConn.From))
            {
                throw new BusinessException(
                    $"连接引用不存在的源节点 '{draftConn.From}'。");
            }

            if (!nodeIds.Contains(draftConn.To))
            {
                throw new BusinessException(
                    $"连接引用不存在的目标节点 '{draftConn.To}'。");
            }

            // 获取源节点和目标节点的端口定义
            var sourceNode = nodes.First(n => n.Id == draftConn.From);
            var targetNode = nodes.First(n => n.Id == draftConn.To);

            var sourceDescriptor = nodeRegistry.GetDescriptor(sourceNode.TypeName);
            var targetDescriptor = nodeRegistry.GetDescriptor(targetNode.TypeName);

            // 解析源端口：如果未指定，使用第一个 Output 端口
            var sourcePortName = draftConn.FromPort;
            if (string.IsNullOrEmpty(sourcePortName))
            {
                var defaultOutput = sourceDescriptor.Ports
                    .FirstOrDefault(p => p.Direction == PortDirection.Output);
                sourcePortName = defaultOutput?.Name
                    ?? throw new BusinessException(
                        $"节点 '{draftConn.From}' 没有可用的输出端口，请指定 FromPort。");
            }
            else
            {
                var portExists = sourceDescriptor.Ports
                    .Any(p => p.Name.Equals(sourcePortName, StringComparison.OrdinalIgnoreCase)
                              && p.Direction == PortDirection.Output);
                if (!portExists)
                {
                    throw new BusinessException(
                        $"节点 '{draftConn.From}' 不存在输出端口 '{sourcePortName}'。");
                }
            }

            // 解析目标端口：如果未指定，使用第一个 Input 端口
            var targetPortName = draftConn.ToPort;
            if (string.IsNullOrEmpty(targetPortName))
            {
                var defaultInput = targetDescriptor.Ports
                    .FirstOrDefault(p => p.Direction == PortDirection.Input);
                targetPortName = defaultInput?.Name
                    ?? throw new BusinessException(
                        $"节点 '{draftConn.To}' 没有可用的输入端口，请指定 ToPort。");
            }
            else
            {
                var portExists = targetDescriptor.Ports
                    .Any(p => p.Name.Equals(targetPortName, StringComparison.OrdinalIgnoreCase)
                              && p.Direction == PortDirection.Input);
                if (!portExists)
                {
                    throw new BusinessException(
                        $"节点 '{draftConn.To}' 不存在输入端口 '{targetPortName}'。");
                }
            }

            connections.Add(new Connection
            {
                SourceNodeId = draftConn.From,
                SourcePortName = sourcePortName,
                TargetNodeId = draftConn.To,
                TargetPortName = targetPortName,
            });
        }

        // ── 4. 自动布局（设计 §5.2 步骤 6）────────────────────
        ApplyAutoLayout(nodes, connections);

        // ── 5. 构建暂态工作流 ├────────────────────────────────
        var workflow = new Workflow
        {
            Name = request.Name,
            ProjectId = request.ProjectId,
            CreatedBy = "ai-assembler",
            IsActive = false,
            Nodes = nodes,
            Connections = connections,
        };

        // ── 5. 校验拓扑（Validate 内部会推导入口节点）──────
        var validationErrors = new List<string>();
        var validationResult = workflowValidator.Validate(workflow);
        if (!validationResult.IsValid)
        {
            validationErrors.AddRange(validationResult.Errors);
        }

        // AI 组装场景必须包含至少一个触发器节点
        workflowValidator.ValidateTriggerNodes(workflow, validationErrors);

        if (validationErrors.Count > 0)
        {
            throw new BusinessException(
                "工作流校验失败：" + string.Join("; ", validationErrors));
        }

        // ── 7. 创建草稿 ──────────────────────────────────────
        var createDto = new CreateWorkflowDto
        {
            Name = workflow.Name,
            ProjectId = workflow.ProjectId,
            CreatedBy = "ai-assembler",
            Nodes = workflow.Nodes.Select(n => WorkflowMapper.ToDto(n)).ToList(),
            Connections = workflow.Connections.Select(c =>
                WorkflowMapper.ToDto(c, c.Id.ToString(), c.SourceNodeId, c.TargetNodeId)).ToList(),
        };

        var draftDto = await workflowService.CreateDraftAsync(createDto, cancellationToken)
            .ConfigureAwait(false);

        return new AssembleWorkflowResult
        {
            DraftId = draftDto.Id,
            Workflow = draftDto,
        };
    }

    /// <summary>
    /// 按节点类型名生成唯一节点 ID（设计 §5.2 步骤 1：id 缺失时自动生成）。
    /// </summary>
    private static string GenerateNodeId(string? typeName, HashSet<string> usedIds)
    {
        var baseId = string.IsNullOrWhiteSpace(typeName) ? "node" : typeName!;
        if (!usedIds.Contains(baseId))
        {
            return baseId;
        }

        var suffix = 2;
        while (usedIds.Contains($"{baseId}{suffix}"))
        {
            suffix++;
        }

        return $"{baseId}{suffix}";
    }

    /// <summary>
    /// 自动布局：按依赖层级（最长路径）从左到右排列节点，补全 PositionX/Y。
    /// 设计 §5.2 步骤 6：AI 不填坐标时由后端自动布局。
    /// </summary>
    private static void ApplyAutoLayout(List<NodeDefinition> nodes, List<Connection> connections)
    {
        const int xSpacing = 320;
        const int ySpacing = 160;

        var nodeById = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var indegree = nodes.ToDictionary(
            n => n.Id,
            n => connections.Count(c =>
                c.TargetNodeId == n.Id && nodeById.ContainsKey(c.SourceNodeId)));
        var adjacency = connections
            .Where(c => nodeById.ContainsKey(c.SourceNodeId) && nodeById.ContainsKey(c.TargetNodeId))
            .ToLookup(c => c.SourceNodeId, c => c.TargetNodeId, StringComparer.Ordinal);

        // Kahn 拓扑排序
        var topo = new List<string>();
        var queue = new Queue<string>(indegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            topo.Add(u);
            foreach (var v in adjacency[u])
            {
                indegree[v]--;
                if (indegree[v] == 0)
                {
                    queue.Enqueue(v);
                }
            }
        }

        // 环中节点兜底追加
        foreach (var node in nodes)
        {
            if (!topo.Contains(node.Id))
            {
                topo.Add(node.Id);
            }
        }

        // 最长路径分层
        var layer = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var u in topo)
        {
            var predecessors = connections
                .Where(c => c.TargetNodeId == u && nodeById.ContainsKey(c.SourceNodeId))
                .Select(c => c.SourceNodeId);
            var maxPred = predecessors.Any() ? predecessors.Max(p => layer.GetValueOrDefault(p, 0)) : -1;
            layer[u] = maxPred + 1;
        }

        // 同层内按出现顺序分配纵坐标
        var layerCursor = new Dictionary<int, int>();
        foreach (var node in nodes.OrderBy(n => layer.GetValueOrDefault(n.Id, 0)))
        {
            var l = layer.GetValueOrDefault(node.Id, 0);
            var row = layerCursor.TryGetValue(l, out var r) ? r : 0;
            layerCursor[l] = row + 1;
            node.PositionX = l * xSpacing;
            node.PositionY = row * ySpacing;
        }
    }
}
