using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流修改服务——通过操作列表对已有工作流进行结构化修改。
/// </summary>
public sealed class WorkflowModificationService(
    INodeRegistry nodeRegistry,
    FlowEngineDbContext dbContext,
    WorkflowValidator workflowValidator,
    IEventBus eventBus,
    AuditEventFactory auditFactory)
    : IWorkflowModificationService
{
    /// <summary>
    /// 对指定工作流应用一组修改操作，创建新的草稿版本。
    /// </summary>
    /// <param name="workflowId">要修改的工作流 ID。</param>
    /// <param name="request">修改操作请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>修改结果。</returns>
    /// <exception cref="BusinessException">工作流不存在或操作无效时抛出。</exception>
    public async Task<ModifyWorkflowResult> ModifyAsync(
        Guid workflowId,
        ModifyWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ── 1. 加载现有工作流 ──────────────────────────────────
        var existing = await dbContext.Workflows
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workflowId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            throw new BusinessException($"工作流 '{workflowId}' 不存在。");
        }

        // ── 2. 深拷贝工作流结构 ────────────────────────────────
        var workflow = DeepClone(existing);
        var diffs = new List<StructuredDiff>();

        // ── 3. 处理每个操作 ────────────────────────────────────
        foreach (var op in request.Operations)
        {
            switch (op.Op.ToLowerInvariant())
            {
                case "modify":
                    ApplyModify(workflow, op, diffs);
                    break;
                case "add":
                    ApplyAdd(workflow, op, diffs);
                    break;
                case "remove":
                    ApplyRemove(workflow, op, diffs);
                    break;
                case "connect":
                    ApplyConnect(workflow, op, diffs);
                    break;
                case "disconnect":
                    ApplyDisconnect(workflow, op, diffs);
                    break;
                default:
                    throw new BusinessException($"不支持的操作类型: '{op.Op}'");
            }
        }

        // ── 4. 运行校验 ────────────────────────────────────────
        var validationErrors = new List<string>();
        var validationResult = workflowValidator.Validate(workflow);
        if (!validationResult.IsValid)
        {
            validationErrors.AddRange(validationResult.Errors);
        }

        // 修改后的工作流必须包含至少一个触发器节点
        workflowValidator.ValidateTriggerNodes(workflow, validationErrors);

        if (validationErrors.Count > 0)
        {
            throw new BusinessException(
                "修改后校验失败：" + string.Join("; ", validationErrors));
        }

        // ── 5. 创建草稿记录（IsActive = false）─────────────────
        var draftWorkflow = new Workflow
        {
            ProjectId = workflow.ProjectId,
            Name = workflow.Name,
            CreatedBy = "ai-modifier",
            IsActive = false,
            Nodes = workflow.Nodes,
            Connections = workflow.Connections,
        };

        dbContext.Workflows.Add(draftWorkflow);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // 发布审计事件（GAP-26）
        var auditEvent = auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.WorkflowCreated,
            "Workflow",
            draftWorkflow.Id,
            new Dictionary<string, object> { ["name"] = draftWorkflow.Name });
        await eventBus.PublishAsync(auditEvent, cancellationToken).ConfigureAwait(false);

        var dto = new WorkflowDto
        {
            Id = draftWorkflow.Id,
            ProjectId = draftWorkflow.ProjectId,
            Name = draftWorkflow.Name,
            Version = draftWorkflow.Version,
            CreatedBy = draftWorkflow.CreatedBy,
            CreatedAt = draftWorkflow.CreatedAt,
            UpdatedAt = draftWorkflow.UpdatedAt,
            IsActive = draftWorkflow.IsActive,
            Nodes = draftWorkflow.Nodes.Select(n => WorkflowMapper.ToDto(n)).ToList(),
            Connections = draftWorkflow.Connections.Select(c =>
                WorkflowMapper.ToDto(c, c.Id.ToString(), c.SourceNodeId, c.TargetNodeId)).ToList(),
        };

        return new ModifyWorkflowResult
        {
            DraftId = draftWorkflow.Id,
            Workflow = dto,
            Diff = diffs,
        };
    }

    /// <summary>
    /// 修改节点参数。
    /// 路径格式：/nodes/{nodeId}/parameters/{fieldName}
    /// </summary>
    private static void ApplyModify(Workflow workflow, WorkflowOperation op, List<StructuredDiff> diffs)
    {
        if (string.IsNullOrEmpty(op.Path))
        {
            throw new BusinessException("modify 操作需要指定 Path。");
        }

        var pathParts = op.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathParts.Length < 2 || pathParts[0] != "nodes")
        {
            throw new BusinessException($"modify 路径格式无效: '{op.Path}'。期望格式: /nodes/{{nodeId}}/parameters/{{field}}");
        }

        var nodeId = pathParts[1];
        var node = workflow.Nodes.FirstOrDefault(n =>
            n.Id.Equals(nodeId, StringComparison.Ordinal));
        if (node is null)
        {
            throw new BusinessException($"节点 '{nodeId}' 不存在。");
        }

        if (pathParts.Length == 3 && pathParts[2] is "name" or "isEntry")
        {
            // 修改节点属性（name, isEntry）
            var fieldName = pathParts[2];
            switch (fieldName)
            {
                case "name":
                    var oldName = node.Name;
                    node.Name = op.Value?.ToString() ?? string.Empty;
                    diffs.Add(new StructuredDiff
                    {
                        Op = "modify", NodeId = nodeId, Field = "name",
                        Before = oldName, After = node.Name,
                    });
                    return;
                case "isEntry":
                    var oldIsEntry = node.IsEntry;
                    node.IsEntry = bool.TryParse(op.Value?.ToString(), out var entry) && entry;
                    diffs.Add(new StructuredDiff
                    {
                        Op = "modify", NodeId = nodeId, Field = "isEntry",
                        Before = oldIsEntry.ToString(), After = node.IsEntry.ToString(),
                    });
                    return;
            }
        }

        if (pathParts.Length >= 4 && pathParts[2] == "parameters")
        {
            var fieldName = string.Join("/", pathParts.Skip(3));
            var oldValue = node.Parameters.TryGetValue(fieldName, out var existing) ? existing : null;

            node.Parameters[fieldName] = op.Value!;
            diffs.Add(new StructuredDiff
            {
                Op = "modify", NodeId = nodeId, Field = $"parameters.{fieldName}",
                Before = oldValue, After = op.Value,
            });
            return;
        }

        throw new BusinessException($"modify 路径未识别: '{op.Path}'。");
    }

    /// <summary>
    /// 添加新节点。
    /// </summary>
    private void ApplyAdd(Workflow workflow, WorkflowOperation op, List<StructuredDiff> diffs)
    {
        if (op.Node is null)
        {
            throw new BusinessException("add 操作需要指定 Node。");
        }

        if (string.IsNullOrWhiteSpace(op.Node.Id))
        {
            throw new BusinessException("新增节点的 ID 不能为空。");
        }

        if (workflow.Nodes.Any(n => n.Id.Equals(op.Node.Id, StringComparison.Ordinal)))
        {
            throw new BusinessException($"节点 ID '{op.Node.Id}' 已存在。");
        }

        if (string.IsNullOrWhiteSpace(op.Node.TypeName))
        {
            throw new BusinessException($"新增节点 '{op.Node.Id}' 的 TypeName 不能为空。");
        }

        // 查找节点类型
        NodeTypeDescriptor descriptor;
        try
        {
            descriptor = nodeRegistry.GetDescriptor(op.Node.TypeName);
        }
        catch (InvalidOperationException)
        {
            throw new BusinessException(
                $"新增节点 '{op.Node.Id}' 使用了未知的节点类型 '{op.Node.TypeName}'。");
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
            Id = op.Node.Id,
            TypeName = op.Node.TypeName,
            Name = op.Node.Id,
            Parameters = op.Node.Parameters,
            Ports = ports,
            PositionX = null,
            PositionY = null,
            IsEntry = false,
        };

        workflow.Nodes.Add(node);
        diffs.Add(new StructuredDiff
        {
            Op = "add", NodeId = op.Node.Id, Field = null,
            Before = null, After = op.Node.Id,
        });

        // 如果指定了 After，创建连接到前一个节点的连接
        if (!string.IsNullOrEmpty(op.After))
        {
            var afterNode = workflow.Nodes.FirstOrDefault(n =>
                n.Id.Equals(op.After, StringComparison.Ordinal));
            if (afterNode is null)
            {
                throw new BusinessException($"After 指定的节点 '{op.After}' 不存在。");
            }

            var afterDescriptor = nodeRegistry.GetDescriptor(afterNode.TypeName);
            var nodeDescriptor = nodeRegistry.GetDescriptor(node.TypeName);

            var sourcePort = afterDescriptor.Ports
                .FirstOrDefault(p => p.Direction == PortDirection.Output);
            var targetPort = nodeDescriptor.Ports
                .FirstOrDefault(p => p.Direction == PortDirection.Input);

            if (sourcePort is not null && targetPort is not null)
            {
                var connection = new Connection
                {
                    SourceNodeId = op.After,
                    SourcePortName = sourcePort.Name,
                    TargetNodeId = op.Node.Id,
                    TargetPortName = targetPort.Name,
                };

                workflow.Connections.Add(connection);
                diffs.Add(new StructuredDiff
                {
                    Op = "connect", NodeId = null, Field = null,
                    Before = null, After = $"{op.After} -> {op.Node.Id}",
                });
            }
        }
    }

    /// <summary>
    /// 移除节点及其所有连接。
    /// </summary>
    private static void ApplyRemove(Workflow workflow, WorkflowOperation op, List<StructuredDiff> diffs)
    {
        if (string.IsNullOrEmpty(op.Path))
        {
            throw new BusinessException("remove 操作需要指定 Path。");
        }

        var pathParts = op.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathParts.Length < 2 || pathParts[0] != "nodes")
        {
            throw new BusinessException($"remove 路径格式无效: '{op.Path}'。期望格式: /nodes/{{nodeId}}");
        }

        var nodeId = pathParts[1];
        var node = workflow.Nodes.FirstOrDefault(n =>
            n.Id.Equals(nodeId, StringComparison.Ordinal));
        if (node is null)
        {
            throw new BusinessException($"节点 '{nodeId}' 不存在。");
        }

        // 移除关联的连接
        var removedConnections = workflow.Connections
            .Where(c => c.SourceNodeId == nodeId || c.TargetNodeId == nodeId)
            .ToList();
        foreach (var conn in removedConnections)
        {
            workflow.Connections.Remove(conn);
        }

        workflow.Nodes.Remove(node);
        diffs.Add(new StructuredDiff
        {
            Op = "remove", NodeId = nodeId, Field = null,
            Before = nodeId, After = null,
        });
    }

    /// <summary>
    /// 在现有节点之间添加连接。
    /// </summary>
    private void ApplyConnect(Workflow workflow, WorkflowOperation op, List<StructuredDiff> diffs)
    {
        if (string.IsNullOrEmpty(op.From))
        {
            throw new BusinessException("connect 操作需要指定 From（源节点 ID）。");
        }

        if (string.IsNullOrEmpty(op.To))
        {
            throw new BusinessException("connect 操作需要指定 To（目标节点 ID）。");
        }

        var sourceNode = workflow.Nodes.FirstOrDefault(n =>
            n.Id.Equals(op.From, StringComparison.Ordinal));
        if (sourceNode is null)
        {
            throw new BusinessException($"源节点 '{op.From}' 不存在。");
        }

        var targetNode = workflow.Nodes.FirstOrDefault(n =>
            n.Id.Equals(op.To, StringComparison.Ordinal));
        if (targetNode is null)
        {
            throw new BusinessException($"目标节点 '{op.To}' 不存在。");
        }

        var sourceDescriptor = nodeRegistry.GetDescriptor(sourceNode.TypeName);
        var targetDescriptor = nodeRegistry.GetDescriptor(targetNode.TypeName);

        var sourcePortName = op.FromPort;
        if (string.IsNullOrEmpty(sourcePortName))
        {
            var defaultOutput = sourceDescriptor.Ports
                .FirstOrDefault(p => p.Direction == PortDirection.Output);
            sourcePortName = defaultOutput?.Name
                ?? throw new BusinessException($"节点 '{op.From}' 没有可用的输出端口。");
        }

        var targetPortName = op.ToPort;
        if (string.IsNullOrEmpty(targetPortName))
        {
            var defaultInput = targetDescriptor.Ports
                .FirstOrDefault(p => p.Direction == PortDirection.Input);
            targetPortName = defaultInput?.Name
                ?? throw new BusinessException($"节点 '{op.To}' 没有可用的输入端口。");
        }

        // 检查重复连接
        var duplicate = workflow.Connections.Any(c =>
            c.SourceNodeId == op.From
            && c.SourcePortName == sourcePortName
            && c.TargetNodeId == op.To
            && c.TargetPortName == targetPortName);
        if (duplicate)
        {
            throw new BusinessException($"连接 '{op.From}' -> '{op.To}' 已存在。");
        }

        var connection = new Connection
        {
            SourceNodeId = op.From,
            SourcePortName = sourcePortName,
            TargetNodeId = op.To,
            TargetPortName = targetPortName,
        };

        workflow.Connections.Add(connection);
        diffs.Add(new StructuredDiff
        {
            Op = "connect", NodeId = null, Field = null,
            Before = null, After = $"{op.From}:{sourcePortName} -> {op.To}:{targetPortName}",
        });
    }

    /// <summary>
    /// 移除两个节点之间的连接。
    /// </summary>
    private static void ApplyDisconnect(Workflow workflow, WorkflowOperation op, List<StructuredDiff> diffs)
    {
        if (string.IsNullOrEmpty(op.From) || string.IsNullOrEmpty(op.To))
        {
            throw new BusinessException("disconnect 操作需要指定 From 和 To。");
        }

        Connection? connection;
        if (!string.IsNullOrEmpty(op.FromPort) || !string.IsNullOrEmpty(op.ToPort))
        {
            connection = workflow.Connections.FirstOrDefault(c =>
                c.SourceNodeId == op.From
                && c.TargetNodeId == op.To
                && (string.IsNullOrEmpty(op.FromPort) || c.SourcePortName == op.FromPort)
                && (string.IsNullOrEmpty(op.ToPort) || c.TargetPortName == op.ToPort));
        }
        else
        {
            connection = workflow.Connections.FirstOrDefault(c =>
                c.SourceNodeId == op.From && c.TargetNodeId == op.To);
        }

        if (connection is null)
        {
            throw new BusinessException($"节点 '{op.From}' 到 '{op.To}' 之间不存在这样的连接。");
        }

        workflow.Connections.Remove(connection);
        diffs.Add(new StructuredDiff
        {
            Op = "disconnect", NodeId = null, Field = null,
            Before = $"{op.From} -> {op.To}", After = null,
        });
    }

    /// <summary>
    /// 深拷贝工作流（含节点和连接的独立副本）。
    /// </summary>
    private static Workflow DeepClone(Workflow source)
    {
        var nodes = source.Nodes.Select(n => new NodeDefinition
        {
            Id = n.Id,
            TypeName = n.TypeName,
            Name = n.Name,
            Parameters = new Dictionary<string, object>(n.Parameters),
            Ports = n.Ports.Select(p => new PortInstance
            {
                Name = p.Name,
                Direction = p.Direction,
                Type = p.Type,
            }).ToList(),
            PositionX = n.PositionX,
            PositionY = n.PositionY,
            IsEntry = n.IsEntry,
            Disabled = n.Disabled,
            RetryPolicy = n.RetryPolicy,
            ErrorStrategy = n.ErrorStrategy,
            Timeout = n.Timeout,
        }).ToList();

        var connections = source.Connections.Select(c => new Connection
        {
            SourceNodeId = c.SourceNodeId,
            SourcePortName = c.SourcePortName,
            TargetNodeId = c.TargetNodeId,
            TargetPortName = c.TargetPortName,
            Condition = c.Condition,
        }).ToList();

        return new Workflow
        {
            Name = source.Name,
            ProjectId = source.ProjectId,
            CreatedBy = source.CreatedBy,
            IsActive = false,
            Nodes = nodes,
            Connections = connections,
            StyleSettings = source.StyleSettings,
        };
    }
}
