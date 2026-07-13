using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Application.Dtos;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流结构化校验服务，返回详细的 <see cref="ValidationError"/> 列表。
/// </summary>
public sealed class WorkflowValidationService(
    INodeRegistry nodeRegistry,
    FlowEngineDbContext dbContext) : IWorkflowValidationService
{
    /// <summary>
    /// 校验工作流定义。
    /// </summary>
    /// <param name="request">校验请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>校验结果。</returns>
    public async Task<ValidateWorkflowResult> ValidateAsync(
        ValidateWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Workflow? workflow = null;
        List<NodeDefinition>? nodes = null;
        List<Connection>? connections = null;

        // ── 1. 加载工作流 ──────────────────────────────────────
        if (request.WorkflowId.HasValue)
        {
            workflow = await dbContext.Workflows
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == request.WorkflowId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (workflow is null)
            {
                return new ValidateWorkflowResult
                {
                    Valid = false,
                    Errors =
                    [
                        new ValidationError
                        {
                            ErrorType = "NotFound",
                            Message = $"工作流 '{request.WorkflowId}' 不存在。",
                        },
                    ],
                };
            }

            nodes = workflow.Nodes;
            connections = workflow.Connections;
        }
        else if (request.Nodes is not null)
        {
            nodes = request.Nodes.Select(WorkflowMapper.ToEntity).ToList();
            connections = request.Connections?.Select(WorkflowMapper.ToEntity).ToList() ?? [];
        }

        // ── 2. 基础空值校验 ────────────────────────────────────
        var errors = new List<ValidationError>();

        if (nodes is null || nodes.Count == 0)
        {
            errors.Add(new ValidationError
            {
                ErrorType = "MissingRequired",
                Message = "工作流不包含任何节点。",
                SuggestedFix = "请添加至少一个触发器节点和一个处理节点。",
            });
            return new ValidateWorkflowResult { Valid = false, Errors = errors, CanAutoFix = false };
        }

        var nodeIds = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

        // ── 3. 端口方向校验 ────────────────────────────────────
        if (connections is not null)
        {
            foreach (var conn in connections)
            {
                if (!nodeIds.Contains(conn.SourceNodeId))
                {
                    errors.Add(new ValidationError
                    {
                        ErrorType = "TopologyError",
                        Message = $"连接引用不存在的源节点 '{conn.SourceNodeId}'。",
                        SuggestedFix = $"请将连接指向有效的节点 ID，可用节点: {string.Join(", ", nodeIds)}",
                    });
                    continue;
                }

                if (!nodeIds.Contains(conn.TargetNodeId))
                {
                    errors.Add(new ValidationError
                    {
                        ErrorType = "TopologyError",
                        Message = $"连接引用不存在的目标节点 '{conn.TargetNodeId}'。",
                        SuggestedFix = $"请将连接指向有效的节点 ID，可用节点: {string.Join(", ", nodeIds)}",
                    });
                    continue;
                }

                var sourceNode = nodes.First(n => n.Id == conn.SourceNodeId);
                var targetNode = nodes.First(n => n.Id == conn.TargetNodeId);

                NodeTypeDescriptor sourceDescriptor;
                NodeTypeDescriptor targetDescriptor;
                try
                {
                    sourceDescriptor = nodeRegistry.GetDescriptor(sourceNode.TypeName);
                    targetDescriptor = nodeRegistry.GetDescriptor(targetNode.TypeName);
                }
                catch (InvalidOperationException)
                {
                    continue; // 类型校验在后面处理
                }

                // 校验源端口
                if (!string.IsNullOrEmpty(conn.SourcePortName))
                {
                    var sourcePort = sourceDescriptor.Ports
                        .FirstOrDefault(p => p.Name.Equals(conn.SourcePortName, StringComparison.OrdinalIgnoreCase));
                    if (sourcePort is null)
                    {
                        errors.Add(new ValidationError
                        {
                            NodeId = conn.SourceNodeId,
                            Field = "sourcePortName",
                            ErrorType = "InvalidType",
                            Message = $"源节点 '{conn.SourceNodeId}' 不存在端口 '{conn.SourcePortName}'。" +
                                      $"可用输出端口: {string.Join(", ", sourceDescriptor.Ports.Where(p => p.Direction == PortDirection.Output).Select(p => p.Name))}",
                            SuggestedFix = string.Join(", ", sourceDescriptor.Ports.Where(p => p.Direction == PortDirection.Output).Select(p => p.Name)),
                        });
                    }
                    else if (sourcePort.Direction != PortDirection.Output)
                    {
                        errors.Add(new ValidationError
                        {
                            NodeId = conn.SourceNodeId,
                            Field = "sourcePortName",
                            ErrorType = "InvalidType",
                            Message = $"源端口 '{conn.SourcePortName}' 不是输出端口。",
                            SuggestedFix = string.Join(", ", sourceDescriptor.Ports.Where(p => p.Direction == PortDirection.Output).Select(p => p.Name)),
                        });
                    }
                }

                // 校验目标端口
                if (!string.IsNullOrEmpty(conn.TargetPortName))
                {
                    var targetPort = targetDescriptor.Ports
                        .FirstOrDefault(p => p.Name.Equals(conn.TargetPortName, StringComparison.OrdinalIgnoreCase));
                    if (targetPort is null)
                    {
                        errors.Add(new ValidationError
                        {
                            NodeId = conn.TargetNodeId,
                            Field = "targetPortName",
                            ErrorType = "InvalidType",
                            Message = $"目标节点 '{conn.TargetNodeId}' 不存在端口 '{conn.TargetPortName}'。" +
                                      $"可用输入端口: {string.Join(", ", targetDescriptor.Ports.Where(p => p.Direction == PortDirection.Input).Select(p => p.Name))}",
                            SuggestedFix = string.Join(", ", targetDescriptor.Ports.Where(p => p.Direction == PortDirection.Input).Select(p => p.Name)),
                        });
                    }
                    else if (targetPort.Direction != PortDirection.Input)
                    {
                        errors.Add(new ValidationError
                        {
                            NodeId = conn.TargetNodeId,
                            Field = "targetPortName",
                            ErrorType = "InvalidType",
                            Message = $"目标端口 '{conn.TargetPortName}' 不是输入端口。",
                            SuggestedFix = string.Join(", ", targetDescriptor.Ports.Where(p => p.Direction == PortDirection.Input).Select(p => p.Name)),
                        });
                    }
                }
            }
        }

        // ── 4. 循环检测 ────────────────────────────────────────
        if (connections is not null && connections.Count > 0)
        {
            var adjacency = nodes.ToDictionary(
                n => n.Id,
                n => connections
                    .Where(c => c.SourceNodeId == n.Id && nodeIds.Contains(c.TargetNodeId))
                    .Select(c => c.TargetNodeId)
                    .ToList());

            var inDegree = nodes.ToDictionary(n => n.Id, _ => 0);
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

            if (visited != nodes.Count)
            {
                var cyclicNodes = inDegree.Where(x => x.Value > 0).Select(x => x.Key).ToList();
                errors.Add(new ValidationError
                {
                    ErrorType = "TopologyError",
                    Message = "工作流存在循环依赖。",
                    NodeId = cyclicNodes.FirstOrDefault(),
                    SuggestedFix = $"请检查以下节点之间的循环引用: {string.Join(", ", cyclicNodes)}",
                });
            }
        }

        // ── 5. 触发器校验 ────────────────────────────────────────
        var hasTrigger = nodes.Any(n =>
        {
            try
            {
                var desc = nodeRegistry.GetDescriptor(n.TypeName);
                return desc.Category.Equals("Trigger", StringComparison.OrdinalIgnoreCase) || desc.DefaultIsEntry;
            }
            catch
            {
                return false;
            }
        });

        if (!hasTrigger)
        {
            errors.Add(new ValidationError
            {
                ErrorType = "MissingRequired",
                Message = "工作流必须至少包含一个触发器节点。",
                SuggestedFix = "请添加一个触发器类型的节点（如 WebhookTrigger, ScheduleTrigger 等）。",
            });
        }

        // ── 6. 必填参数校验 ──────────────────────────────────────
        foreach (var node in nodes)
        {
            NodeTypeDescriptor descriptor;
            try
            {
                descriptor = nodeRegistry.GetDescriptor(node.TypeName);
            }
            catch (InvalidOperationException)
            {
                errors.Add(new ValidationError
                {
                    NodeId = node.Id,
                    ErrorType = "InvalidType",
                    Message = $"节点 '{node.Id}' 使用了未知的节点类型 '{node.TypeName}'。",
                    SuggestedFix = "请使用目录 API 查询可用的节点类型。",
                });
                continue;
            }

            foreach (var param in descriptor.Parameters.Where(p => p.Required))
            {
                // 带默认值的必填参数（如枚举）本质上是可选的，AI 不填时使用默认值即可，
                // 不应判为缺失（task-013 P5a）。
                if (param.DefaultValue is not null)
                {
                    continue;
                }

                if (!node.Parameters.TryGetValue(param.Name, out var value) || value is null)
                {
                    errors.Add(new ValidationError
                    {
                        NodeId = node.Id,
                        Field = param.Name,
                        ErrorType = "MissingRequired",
                        Message = $"节点 '{node.Id}' 缺少必填参数 '{param.DisplayName}'。",
                        SuggestedFix = $"请设置 '{param.Name}' 参数。",
                        Schema = NodeDefinitionAdapter.ConvertParameterType(param),
                    });
                }
            }
        }

        // ── 7. 参数值校验（类型/枚举范围）──────────────────────────
        foreach (var node in nodes)
        {
            NodeTypeDescriptor descriptor;
            try
            {
                descriptor = nodeRegistry.GetDescriptor(node.TypeName);
            }
            catch (InvalidOperationException)
            {
                continue; // 已在步骤 6 报告中
            }

            // Task 4：表达式参数扫描接入 MCP validate_workflow 链路，
            // 避免 AI 生成的 {{ }} mustache 模板 / 非法 JS 表达式拿到虚假绿灯（Valid=true）。
            // mustache 词法扫描为首要防线（带/不带引号均命中），JS 编译检查为通用语法网。
            var paramsNode = JsonSerializer.SerializeToNode(node.Parameters) as JsonObject;
            if (paramsNode is not null)
            {
                var scanErrors = new List<string>();
                WorkflowDraftValidator.CollectMustacheErrors(paramsNode, node.Id, scanErrors);
                WorkflowDraftValidator.CollectExpressionSyntaxErrors(paramsNode, descriptor, node.Id, scanErrors);
                foreach (var msg in scanErrors)
                {
                    errors.Add(new ValidationError
                    {
                        NodeId = node.Id,
                        ErrorType = "InvalidExpression",
                        Message = msg,
                        SuggestedFix = "把 {{ }} mustache 改为 JavaScript 表达式拼接，例如 'https://api.com/path?token=' + $json.token",
                    });
                }
            }

            foreach (var param in descriptor.Parameters)
            {
                if (!node.Parameters.TryGetValue(param.Name, out var value) || value is null)
                    continue; // 已在步骤 6 报告中

                // Options 类型：校验值是否在允许范围内
                if (param.Type == ParameterType.Options && param.Options.Count > 0)
                {
                    var allowedValues = param.Options
                        .Select(o => o.Value?.ToString())
                        .Where(v => v is not null)
                        .Cast<string>()
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var strValue = ExtractParameterString(value);
                    if (strValue is not null && !allowedValues.Contains(strValue))
                    {
                        errors.Add(new ValidationError
                        {
                            NodeId = node.Id,
                            Field = param.Name,
                            ErrorType = "InvalidValue",
                            Message = $"节点 '{node.Id}' 参数 '{param.DisplayName}' 的值 '{strValue}' 不在允许范围内。" +
                                      $"允许的值: {string.Join(", ", allowedValues)}",
                            SuggestedFix = $"请将 '{param.Name}' 设置为允许的值之一: {string.Join(", ", allowedValues)}",
                        });
                    }
                }
            }
        }

        var canAutoFix = errors.All(e =>
            e.ErrorType is "MissingRequired" or "InvalidType" or "InvalidValue");

        return new ValidateWorkflowResult
        {
            Valid = errors.Count == 0,
            Errors = errors,
            CanAutoFix = canAutoFix,
        };
    }

    private static string? ExtractParameterString(object? value)
    {
        return value switch
        {
            null => null,
            string s => s,
            JsonElement element => element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            },
            _ => value.ToString(),
        };
    }
}
