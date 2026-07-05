using System.Collections;
using System.Text.Json;
using FlowEngine.Application.Dtos;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Executor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流 Dry-Run 服务，在无副作用模式下预演工作流执行。
/// </summary>
public sealed class WorkflowDryRunService(
    FlowEngineDbContext dbContext,
    INodeRegistry nodeRegistry,
    NodeExecutionContextFactory contextFactory,
    ILogger<WorkflowDryRunService> logger)
{
    /// <summary>
    /// 对指定工作流执行 Dry-Run。
    /// </summary>
    /// <param name="workflowId">工作流定义 ID。</param>
    /// <param name="input">可选的触发输入数据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Dry-Run 结果；工作流不存在时返回 null。</returns>
    public async Task<DryRunWorkflowResponseDto?> DryRunAsync(
        Guid workflowId,
        object? input = null,
        CancellationToken cancellationToken = default)
    {
        var workflow = await dbContext.Workflows
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workflowId, cancellationToken)
            .ConfigureAwait(false);

        if (workflow is null)
        {
            return null;
        }

        var executionRecord = new ExecutionRecord
        {
            WorkflowDefinitionId = workflowId,
            ProjectId = workflow.ProjectId,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Running,
            NodeRecords = []
        };

        var nodeRecords = new List<DryRunNodeRecordDto>();
        var warnings = new List<string>();
        var successfulOutputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);
        var latestBatches = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);
        var nodeOutputs = new Dictionary<Guid, DataBatch>();
        var pendingInputs = new Dictionary<Guid, Dictionary<string, DataBatch>>();
        var processedNodes = new HashSet<Guid>();

        var nodeMap = workflow.Nodes.ToDictionary(n => n.Id);
        var incomingByTarget = workflow.Connections
            .GroupBy(c => c.TargetNodeId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var outgoingBySource = workflow.Connections
            .GroupBy(c => c.SourceNodeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var queue = new Queue<Guid>();
        var hasIncoming = workflow.Connections.Select(c => c.TargetNodeId).ToHashSet();

        foreach (var node in workflow.Nodes)
        {
            var nodeType = nodeRegistry.Get(node.TypeName);
            if (node.IsEntry || nodeType.DefaultIsEntry || !hasIncoming.Contains(node.Id))
            {
                queue.Enqueue(node.Id);
            }
        }

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (!nodeMap.TryGetValue(nodeId, out var node))
            {
                continue;
            }

            var nodeType = nodeRegistry.Get(node.TypeName);
            var inputPortNames = nodeType.Ports
                .Where(p => p.Direction == PortDirection.Input)
                .Select(p => p.Name)
                .ToList();

            var inputs = CollectInputs(nodeId, inputPortNames, incomingByTarget, nodeOutputs, pendingInputs);

            if (inputs.Count == 0 && inputPortNames.Count > 0)
            {
                inputs[inputPortNames[0]] = CreateDataBatch(input);
            }

            if (inputPortNames.Count > 1 && inputPortNames.Any(p => !inputs.ContainsKey(p)))
            {
                continue;
            }

            processedNodes.Add(nodeId);

            DryRunNodeRecordDto record;
            if (nodeType is ISupportsDryRun)
            {
                record = await ExecuteNodeAsync(
                    workflow,
                    executionRecord,
                    node,
                    nodeType,
                    inputs,
                    successfulOutputs,
                    latestBatches,
                    cancellationToken).ConfigureAwait(false);

                if (record.Output is DataBatch outputBatch)
                {
                    nodeOutputs[nodeId] = outputBatch;
                    latestBatches[node.Name] = outputBatch;
                    if (record.Success)
                    {
                        successfulOutputs[node.Name] = outputBatch;
                    }
                }
            }
            else
            {
                var warning = $"节点 '{node.Name}' ({node.TypeName}) 不支持 Dry-Run，已跳过。";
                warnings.Add(warning);
                logger.LogDebug("Dry-run skipped node {NodeName} ({NodeType})", node.Name, node.TypeName);

                var passThrough = inputs.Values.FirstOrDefault() ?? new DataBatch();
                nodeOutputs[nodeId] = passThrough;
                latestBatches[node.Name] = passThrough;

                record = new DryRunNodeRecordDto
                {
                    NodeDefinitionId = node.Id,
                    NodeName = node.Name,
                    NodeType = node.TypeName,
                    Skipped = true,
                    SkipReason = warning,
                    Success = true
                };
            }

            nodeRecords.Add(record);

            if (outgoingBySource.TryGetValue(nodeId, out var outgoingConnections))
            {
                foreach (var connection in outgoingConnections)
                {
                    if (!queue.Contains(connection.TargetNodeId) && !processedNodes.Contains(connection.TargetNodeId))
                    {
                        queue.Enqueue(connection.TargetNodeId);
                    }
                }
            }
        }

        return new DryRunWorkflowResponseDto
        {
            WorkflowId = workflowId,
            Status = "Completed",
            NodeRecords = nodeRecords,
            Warnings = warnings
        };
    }

    private static Dictionary<string, DataBatch> CollectInputs(
        Guid nodeId,
        IReadOnlyList<string> inputPortNames,
        IReadOnlyDictionary<Guid, List<Connection>> incomingByTarget,
        IReadOnlyDictionary<Guid, DataBatch> nodeOutputs,
        IDictionary<Guid, Dictionary<string, DataBatch>> pendingInputs)
    {
        var inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);

        if (incomingByTarget.TryGetValue(nodeId, out var incomingConnections))
        {
            foreach (var connection in incomingConnections)
            {
                if (nodeOutputs.TryGetValue(connection.SourceNodeId, out var sourceOutput))
                {
                    inputs[connection.TargetPortName] = sourceOutput;
                }
            }
        }

        if (inputPortNames.Count > 1 && inputPortNames.Any(p => !inputs.ContainsKey(p)))
        {
            if (pendingInputs.TryGetValue(nodeId, out var pending))
            {
                foreach (var (portName, batch) in pending)
                {
                    inputs[portName] = batch;
                }
            }

            if (inputPortNames.Any(p => !inputs.ContainsKey(p)))
            {
                pendingInputs[nodeId] = new Dictionary<string, DataBatch>(inputs, StringComparer.OrdinalIgnoreCase);
            }
        }

        return inputs;
    }

    private async Task<DryRunNodeRecordDto> ExecuteNodeAsync(
        Workflow workflow,
        ExecutionRecord executionRecord,
        NodeDefinition node,
        INodeType nodeType,
        IReadOnlyDictionary<string, DataBatch> inputs,
        IReadOnlyDictionary<string, DataBatch> successfulOutputs,
        IReadOnlyDictionary<string, DataBatch> latestBatches,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = await contextFactory.CreateAsync(
                workflow,
                executionRecord,
                node,
                nodeType,
                inputs,
                successfulOutputs,
                latestBatches,
                runIndex: 0,
                cancellationToken).ConfigureAwait(false);

            var result = await nodeType.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

            return new DryRunNodeRecordDto
            {
                NodeDefinitionId = node.Id,
                NodeName = node.Name,
                NodeType = node.TypeName,
                Skipped = false,
                Success = result.Success,
                Output = result.Output
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Dry-run node {NodeName} ({NodeType}) execution failed.", node.Name, node.TypeName);
            return new DryRunNodeRecordDto
            {
                NodeDefinitionId = node.Id,
                NodeName = node.Name,
                NodeType = node.TypeName,
                Skipped = false,
                Success = false,
                Output = new DataBatch
                {
                    Items =
                    [
                        new DataItem
                        {
                            Success = false,
                            Error = new NodeError
                            {
                                Code = ex.GetType().Name,
                                Message = ex.Message,
                                NodeDefinitionId = node.Id,
                                StackTrace = ex.StackTrace
                            }
                        }
                    ]
                }
            };
        }
    }

    private static DataBatch CreateDataBatch(object? payload)
    {
        if (payload is DataBatch batch)
        {
            return batch;
        }

        if (payload is DataItem item)
        {
            return new DataBatch { Items = [item] };
        }

        if (payload is null)
        {
            return new DataBatch
            {
                Items = [new DataItem { Data = null, Success = true, SourceIndex = 0 }]
            };
        }

        if (payload is IEnumerable enumerable && payload is not string)
        {
            var items = new List<DataItem>();
            var index = 0;
            foreach (var value in enumerable)
            {
                items.Add(new DataItem
                {
                    Data = JsonSerializer.SerializeToNode(value, JsonDefaults.Options),
                    Success = true,
                    SourceIndex = index++
                });
            }

            return new DataBatch { Items = items };
        }

        var data = JsonSerializer.SerializeToNode(payload, JsonDefaults.Options);
        return new DataBatch
        {
            Items = [new DataItem { Data = data, Success = true, SourceIndex = 0 }]
        };
    }
}
