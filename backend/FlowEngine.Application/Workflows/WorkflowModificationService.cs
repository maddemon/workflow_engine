using System.Text.Json;
using FlowEngine.Core;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Abstractions;
using Mapster;
using FlowEngine.Core.Authorization;
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
    AuditEventFactory auditFactory,
    IAuthorizationGuard authGuard)
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

        // RBAC：修改前校验工作流写权限。
        await authGuard.RequireAccessAsync(ResourceKind.Workflow, workflowId, Operation.Write, cancellationToken).ConfigureAwait(false);

        // ── 1. 加载现有工作流（受跟踪，单一来源，避免二次加载造成丢失更新）──
        var existing = await dbContext.Workflows
            .FirstOrDefaultAsync(w => w.Id == workflowId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException($"Workflow '{workflowId}' does not exist.");
        }

        // ── 2. 深拷贝工作流结构（在脱离跟踪的副本上计算修改，避免污染被跟踪实体）──
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
                    // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
                    throw new BusinessException($"Unsupported operation type: '{op.Op}'");
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
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException(
                "Validation failed after modification: " + string.Join("; ", validationErrors));
        }

        // ── 5. 就地更新工作流（read-modify-write，单一受跟踪加载，避免覆盖并发更新）──
        existing.Name = workflow.Name;
        existing.Diff = diffs;
        existing.Nodes = workflow.Nodes;
        existing.Connections = workflow.Connections;
        // 仅当存在"实质内容变更"时才递增版本号（修复：原实现无条件 Version += 1，
        // 导致 no-op 修改也会膨胀版本）。参数为 JSON 列，diff 的 Before/After 经反序列化后
        // 可能为 JsonElement，故用值语义（JSON 文本）比较而非引用/类型比较。
        var hasContentChanged = diffs.Any(d => !ValuesEqual(d.Before, d.After));
        if (hasContentChanged)
        {
            existing.Version += 1;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // 发布审计事件（GAP-26）
        var auditEvent = auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.WorkflowUpdated,
            "Workflow",
            existing.Id,
            new Dictionary<string, object> { ["name"] = existing.Name });
        await eventBus.PublishAsync(auditEvent, cancellationToken).ConfigureAwait(false);

        var dto = existing.Adapt<WorkflowDto>() with
        {
            Diff = diffs,
        };

        return new ModifyWorkflowResult
        {
            // 修改操作在已有工作流上就地更新，DraftId 即被更新的工作流实体 Id，
            // 确认时需用它定位同一条记录。
            DraftId = existing.Id,
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
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException("The modify operation requires a Path.");
        }

        var pathParts = op.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathParts.Length < 2 || pathParts[0] != "nodes")
        {
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException($"Invalid modify path format: '{op.Path}'. Expected format: /nodes/{{nodeId}}/parameters/{{field}}");
        }

        var nodeId = pathParts[1];
        var node = workflow.Nodes.FirstOrDefault(n =>
            n.Id.Equals(nodeId, StringComparison.Ordinal));
        if (node is null)
        {
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException($"Node '{nodeId}' does not exist.");
        }

        if (pathParts.Length == 3 && pathParts[2] is "name" or "isEntry" or "disabled")
        {
            // 修改节点属性（name, isEntry, disabled）
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
                case "disabled":
                    var oldDisabled = node.Disabled;
                    node.Disabled = bool.TryParse(op.Value?.ToString(), out var disabled) && disabled;
                    diffs.Add(new StructuredDiff
                    {
                        Op = "modify", NodeId = nodeId, Field = "disabled",
                        Before = oldDisabled.ToString(), After = node.Disabled.ToString(),
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

        // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException($"Modify path not recognized: '{op.Path}'.");
    }

    /// <summary>
    /// 添加新节点。
    /// </summary>
    private void ApplyAdd(Workflow workflow, WorkflowOperation op, List<StructuredDiff> diffs)
    {
        if (op.Node is null)
        {
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException("The add operation requires a Node.");
        }

        if (string.IsNullOrWhiteSpace(op.Node.Id))
        {
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException("The new node ID cannot be empty.");
        }

        if (workflow.Nodes.Any(n => n.Id.Equals(op.Node.Id, StringComparison.Ordinal)))
        {
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException($"Node ID '{op.Node.Id}' already exists.");
        }

        if (string.IsNullOrWhiteSpace(op.Node.TypeName))
        {
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException($"TypeName for the new node '{op.Node.Id}' cannot be empty.");
        }

        // 查找节点类型
        NodeTypeDescriptor descriptor;
        try
        {
            descriptor = nodeRegistry.GetDescriptor(op.Node.TypeName);
        }
        catch (InvalidOperationException)
            {
                // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
                throw new BusinessException(
                    $"The new node '{op.Node.Id}' uses an unknown node type '{op.Node.TypeName}'.");
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
            Disabled = op.Node.Disabled,
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
                // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException($"The node specified by After '{op.After}' does not exist.");
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
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException("The remove operation requires a Path.");
        }

        var pathParts = op.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathParts.Length < 2 || pathParts[0] != "nodes")
        {
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException($"Invalid remove path format: '{op.Path}'. Expected format: /nodes/{{nodeId}}");
        }

        var nodeId = pathParts[1];
        var node = workflow.Nodes.FirstOrDefault(n =>
            n.Id.Equals(nodeId, StringComparison.Ordinal));
        if (node is null)
        {
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException($"Node '{nodeId}' does not exist.");
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
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException("The connect operation requires From (source node ID).");
        }

        if (string.IsNullOrEmpty(op.To))
        {
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException("The connect operation requires To (target node ID).");
        }

        var sourceNode = workflow.Nodes.FirstOrDefault(n =>
            n.Id.Equals(op.From, StringComparison.Ordinal));
        if (sourceNode is null)
        {
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException($"Source node '{op.From}' does not exist.");
        }

        var targetNode = workflow.Nodes.FirstOrDefault(n =>
            n.Id.Equals(op.To, StringComparison.Ordinal));
        if (targetNode is null)
        {
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException($"Target node '{op.To}' does not exist.");
        }

        var sourceDescriptor = nodeRegistry.GetDescriptor(sourceNode.TypeName);
        var targetDescriptor = nodeRegistry.GetDescriptor(targetNode.TypeName);

        var sourcePortName = op.FromPort;
        if (string.IsNullOrEmpty(sourcePortName))
        {
            var defaultOutput = sourceDescriptor.Ports
                .FirstOrDefault(p => p.Direction == PortDirection.Output);
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            sourcePortName = defaultOutput?.Name
                ?? throw new BusinessException($"Node '{op.From}' has no available output port.");
        }
        else
        {
            var matchedPort = sourceDescriptor.Ports
                .FirstOrDefault(p => p.Name.Equals(sourcePortName, StringComparison.OrdinalIgnoreCase)
                                  && p.Direction == PortDirection.Output);
            if (matchedPort is null)
            {
                // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
                throw new BusinessException(
                    $"Node '{op.From}' does not have output port '{sourcePortName}'.");
            }
            // 归一化：使用规范的端口名
            sourcePortName = matchedPort.Name;
        }

        var targetPortName = op.ToPort;
        if (string.IsNullOrEmpty(targetPortName))
        {
            var defaultInput = targetDescriptor.Ports
                .FirstOrDefault(p => p.Direction == PortDirection.Input);
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            targetPortName = defaultInput?.Name
                ?? throw new BusinessException($"Node '{op.To}' has no available input port.");
        }
        else
        {
            var matchedPort = targetDescriptor.Ports
                .FirstOrDefault(p => p.Name.Equals(targetPortName, StringComparison.OrdinalIgnoreCase)
                                  && p.Direction == PortDirection.Input);
            if (matchedPort is null)
            {
                // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
                throw new BusinessException(
                    $"Node '{op.To}' does not have input port '{targetPortName}'.");
            }
            // 归一化：使用规范的端口名
            targetPortName = matchedPort.Name;
        }

        // 检查重复连接
        var duplicate = workflow.Connections.Any(c =>
            c.SourceNodeId == op.From
            && c.SourcePortName == sourcePortName
            && c.TargetNodeId == op.To
            && c.TargetPortName == targetPortName);
        if (duplicate)
        {
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException($"Connection '{op.From}' -> '{op.To}' already exists.");
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
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException("The disconnect operation requires From and To.");
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
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException($"No such connection exists between nodes '{op.From}' and '{op.To}'.");
        }

        workflow.Connections.Remove(connection);
        diffs.Add(new StructuredDiff
        {
            Op = "disconnect", NodeId = null, Field = null,
            Before = $"{op.From} -> {op.To}", After = null,
        });
    }

    /// <summary>
    /// 比较两个差异值是否在"内容"上相等。参数为 JSON 列，diff 的 Before/After 经反序列化后
    /// 可能是 <see cref="JsonElement"/>（而请求值通常是 <see cref="string"/>），直接 <see cref="object.Equals(object)"/>
    /// 会因类型不同误判为不等。这里统一按 JSON 文本比较，使 no-op 修改被正确识别为"无变化"。
    /// </summary>
    private static bool ValuesEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is JsonElement l && right is JsonElement r)
        {
            return l.GetRawText() == r.GetRawText();
        }

        // 任一侧为 JsonElement 时，取其对等 JSON 文本后再与另一侧比较。
        var leftJson = left is JsonElement le ? le.GetRawText() : JsonSerializer.Serialize(left, JsonDefaults.Options);
        var rightJson = right is JsonElement re ? re.GetRawText() : JsonSerializer.Serialize(right, JsonDefaults.Options);
        return leftJson == rightJson;
    }

    /// <summary>
    /// 深拷贝工作流（含节点和连接的独立副本）。
    /// </summary>
    private static Workflow DeepClone(Workflow source)
    {
        // Adapt 执行浅拷贝；通过序列化-反序列化确保集合和嵌套对象完全独立。
        var json = JsonSerializer.Serialize(source, JsonDefaults.Options);
        var clone = JsonSerializer.Deserialize<Workflow>(json, JsonDefaults.Options)!;
        clone.IsActive = false;
        return clone;
    }
}
