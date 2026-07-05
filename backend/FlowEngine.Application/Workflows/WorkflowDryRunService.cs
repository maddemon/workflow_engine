using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Application.Dtos;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Executor;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流 Dry-Run 服务，直接在内存中构建 DSL 定义并执行，不持久化任何记录。
/// </summary>
public sealed class WorkflowDryRunService(
    INodeRegistry nodeRegistry,
    NodeExecutionContextFactory contextFactory,
    ILogger<WorkflowDryRunService> logger)
{
    /// <summary>
    /// 对传入的 DSL 工作流执行 Dry-Run。
    /// </summary>
    /// <param name="request">Dry-Run 请求，包含节点、连接、输入与临时凭据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行结果 DTO。</returns>
    public async Task<ExecutionDto> DryRunAsync(
        DryRunWorkflowRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workflow = BuildWorkflow(request);
        var credentialAccessor = BuildCredentialAccessor(request.Credentials);
        var sensitiveValues = ExtractSensitiveValues(request.Credentials);
        await PreResolveCredentialParameters(workflow, credentialAccessor, cancellationToken).ConfigureAwait(false);
        var executionRecord = new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            ProjectId = workflow.ProjectId,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Running,
            NodeRecords = []
        };

        var nodeMap = workflow.Nodes.ToDictionary(n => n.Id);
        var incomingByTarget = workflow.Connections
            .GroupBy(c => c.TargetNodeId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var outgoingBySource = workflow.Connections
            .GroupBy(c => c.SourceNodeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var nodeOutputs = new Dictionary<Guid, DataBatch>();
        var successfulOutputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);
        var latestBatches = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);
        var waitingArea = new DryRunWaitingArea();
        var processedNodes = new HashSet<Guid>();
        var queue = new Queue<Guid>();

        var triggerBatch = CreateDataBatch(request.Inputs);
        EnqueueEntryNodes(workflow, queue, processedNodes, triggerBatch, nodeOutputs, latestBatches);

        var failed = false;

        while (queue.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var nodeId = queue.Dequeue();
            if (!nodeMap.TryGetValue(nodeId, out var node))
            {
                continue;
            }

            if (processedNodes.Contains(nodeId))
            {
                continue;
            }

            var nodeType = nodeRegistry.Get(node.TypeName);
            var inputPortNames = GetInputPortNames(nodeType);
            var inputs = CollectInputs(nodeId, inputPortNames, incomingByTarget, nodeOutputs, waitingArea);

            if (inputPortNames.Count > 1 && inputPortNames.Any(p => !inputs.ContainsKey(p)))
            {
                // 多输入端口未全部就绪，继续等待。
                continue;
            }

            processedNodes.Add(nodeId);

            var context = await contextFactory.CreateAsync(
                workflow,
                executionRecord,
                node,
                nodeType,
                inputs,
                successfulOutputs,
                latestBatches,
                runIndex: 0,
                cancellationToken,
                credentialAccessor).ConfigureAwait(false);

            NodeExecutionResult result;
            try
            {
                result = await ExecuteNodeAsync(node, nodeType, context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Dry-run 节点 {NodeName} ({NodeType}) 执行异常。", node.Name, node.TypeName);
                var error = new NodeError
                {
                    Code = ex.GetType().Name,
                    Message = ex.Message,
                    NodeDefinitionId = node.Id,
                    StackTrace = ex.StackTrace
                };
                result = new NodeExecutionResult
                {
                    Success = false,
                    Error = error,
                    Output = new DataBatch { Items = [new DataItem { Success = false, Error = error }] }
                };
            }

            var record = BuildNodeExecutionRecord(node.Id, 0, inputs, result, context, sensitiveValues);
            executionRecord.NodeRecords.Add(record);

            nodeOutputs[nodeId] = result.Output;
            latestBatches[node.Name] = result.Output;
            if (result.Success)
            {
                successfulOutputs[node.Name] = result.Output;
            }
            else
            {
                failed = true;
            }

            if (outgoingBySource.TryGetValue(nodeId, out var outgoingConnections))
            {
                foreach (var connection in outgoingConnections)
                {
                    if (!processedNodes.Contains(connection.TargetNodeId) && !queue.Contains(connection.TargetNodeId))
                    {
                        queue.Enqueue(connection.TargetNodeId);
                    }
                }
            }
        }

        executionRecord.CompletedAt = DateTime.UtcNow;
        executionRecord.Status = failed ? ExecutionStatus.Failed : ExecutionStatus.DryRunCompleted;

        return MapToDto(executionRecord);
    }

    private static Workflow BuildWorkflow(DryRunWorkflowRequestDto request)
    {
        var nodeIdMap = new Dictionary<string, Guid>();
        var nodes = request.Nodes.Select(n => WorkflowMapper.ToEntity(n, nodeIdMap)).ToList();
        var connections = request.Connections.Select(c => WorkflowMapper.ToEntity(c, nodeIdMap)).ToList();

        return new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "dry-run",
            CreatedBy = "dry-run",
            IsActive = true,
            Version = 1,
            Nodes = nodes,
            Connections = connections
        };
    }

    private static TemporaryCredentialAccessor BuildCredentialAccessor(IReadOnlyCollection<DryRunCredentialDto>? credentials)
    {
        var values = new Dictionary<string, CredentialValue>(StringComparer.OrdinalIgnoreCase);
        if (credentials is not null)
        {
            foreach (var credential in credentials)
            {
                values[credential.Name] = new CredentialValue
                {
                    Name = credential.Name,
                    Type = credential.Type,
                    Fields = credential.Fields,
                    BinaryFields = []
                };
            }
        }

        return new TemporaryCredentialAccessor(values);
    }

    private async Task PreResolveCredentialParameters(Workflow workflow, TemporaryCredentialAccessor credentialAccessor, CancellationToken cancellationToken)
    {
        foreach (var node in workflow.Nodes)
        {
            var descriptor = nodeRegistry.GetDescriptor(node.TypeName);
            var credentialParameters = descriptor.Parameters
                .Where(p => p.Type == ParameterType.Credential)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var key in node.Parameters.Keys.ToList())
            {
                if (!credentialParameters.Contains(key))
                {
                    continue;
                }

                var value = node.Parameters[key];
                if (value is string credentialName)
                {
                    var credential = await credentialAccessor.GetCredentialByNameAsync(credentialName, cancellationToken).ConfigureAwait(false);
                    if (credential is not null)
                    {
                        node.Parameters[key] = credential;
                    }
                }
            }
        }
    }

    private void EnqueueEntryNodes(
        Workflow workflow,
        Queue<Guid> queue,
        HashSet<Guid> processedNodes,
        DataBatch triggerBatch,
        Dictionary<Guid, DataBatch> nodeOutputs,
        Dictionary<string, DataBatch> latestBatches)
    {
        var hasIncoming = workflow.Connections.Select(c => c.TargetNodeId).ToHashSet();

        foreach (var node in workflow.Nodes)
        {
            var nodeType = nodeRegistry.Get(node.TypeName);
            var isEntry = node.IsEntry || nodeType.DefaultIsEntry || !hasIncoming.Contains(node.Id);
            if (!isEntry)
            {
                continue;
            }

            var inputPorts = GetInputPortNames(nodeType);
            if (inputPorts.Count > 0)
            {
                nodeOutputs[node.Id] = triggerBatch;
                latestBatches[node.Name] = triggerBatch;
            }

            if (!processedNodes.Contains(node.Id) && !queue.Contains(node.Id))
            {
                queue.Enqueue(node.Id);
            }
        }
    }

    private static IReadOnlyList<string> GetInputPortNames(INodeType nodeType)
    {
        return nodeType.Ports
            .Where(p => p.Direction == PortDirection.Input)
            .Select(p => p.Name)
            .ToList();
    }

    private static Dictionary<string, DataBatch> CollectInputs(
        Guid nodeId,
        IReadOnlyList<string> inputPortNames,
        IReadOnlyDictionary<Guid, List<Connection>> incomingByTarget,
        IReadOnlyDictionary<Guid, DataBatch> nodeOutputs,
        DryRunWaitingArea waitingArea)
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
            var pending = waitingArea.Take(nodeId);
            foreach (var (portName, batch) in pending)
            {
                inputs[portName] = batch;
            }

            var missing = inputPortNames.Where(p => !inputs.ContainsKey(p)).ToList();
            if (missing.Count > 0)
            {
                waitingArea.Store(nodeId, inputs);
            }
        }

        return inputs;
    }

    private async Task<NodeExecutionResult> ExecuteNodeAsync(
        NodeDefinition node,
        INodeType nodeType,
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var timeout = node.Timeout;
        if (timeout is null || timeout <= TimeSpan.Zero)
        {
            return await nodeType.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout.Value);
        try
        {
            return await nodeType.ExecuteAsync(context, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var error = new NodeError
            {
                Code = "Timeout",
                Message = $"节点执行超时，超时时间：{timeout.Value.TotalMilliseconds}ms。",
                NodeDefinitionId = node.Id
            };
            return new NodeExecutionResult
            {
                Success = false,
                Error = error,
                Output = new DataBatch { Items = [new DataItem { Success = false, Error = error }] }
            };
        }
    }

    private static NodeExecutionRecord BuildNodeExecutionRecord(
        Guid nodeDefinitionId,
        int runIndex,
        IReadOnlyDictionary<string, DataBatch> inputs,
        NodeExecutionResult output,
        NodeExecutionContext context,
        IReadOnlySet<string> sensitiveValues)
    {
        return new NodeExecutionRecord
        {
            Id = context.NodeExecutionRecordId,
            NodeDefinitionId = nodeDefinitionId,
            RunIndex = runIndex,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Inputs = inputs.ToDictionary(kv => kv.Key, kv => SanitizeDataBatch(kv.Value, sensitiveValues), StringComparer.OrdinalIgnoreCase),
            Output = SanitizeOutput(output, sensitiveValues),
            RawParameters = SanitizeParameters(context.RawParameters, sensitiveValues),
            ResolvedParameters = SanitizeParameters(context.ResolvedParameters, sensitiveValues)
        };
    }

    private static HashSet<string> ExtractSensitiveValues(IReadOnlyCollection<DryRunCredentialDto>? credentials)
    {
        var values = new HashSet<string>();
        if (credentials is null)
        {
            return values;
        }

        foreach (var credential in credentials)
        {
            foreach (var fieldValue in credential.Fields.Values)
            {
                values.Add(fieldValue);
            }
        }

        return values;
    }

    private static NodeExecutionResult SanitizeOutput(NodeExecutionResult result, IReadOnlySet<string> sensitiveValues)
    {
        return new NodeExecutionResult
        {
            Success = result.Success,
            Output = SanitizeDataBatch(result.Output, sensitiveValues),
            Error = result.Error,
            BranchIndex = result.BranchIndex
        };
    }

    private static DataBatch SanitizeDataBatch(DataBatch batch, IReadOnlySet<string> sensitiveValues)
    {
        if (sensitiveValues.Count == 0)
        {
            return batch;
        }

        var sanitizedBatch = new DataBatch { Items = [] };
        foreach (var item in batch.Items)
        {
            sanitizedBatch.Items.Add(new DataItem
            {
                Data = item.Data is null ? null : SanitizeJsonNode(item.Data.DeepClone(), sensitiveValues),
                Success = item.Success,
                Error = item.Error,
                SourceIndex = item.SourceIndex,
                AttachmentId = item.AttachmentId
            });
        }

        return sanitizedBatch;
    }

    private static JsonNode? SanitizeJsonNode(JsonNode? node, IReadOnlySet<string> sensitiveValues)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                var sanitizedProperty = SanitizeJsonNode(property.Value, sensitiveValues);
                if (!ReferenceEquals(property.Value, sanitizedProperty))
                {
                    jsonObject[property.Key] = sanitizedProperty;
                }
            }

            return jsonObject;
        }

        if (node is JsonArray jsonArray)
        {
            for (var i = 0; i < jsonArray.Count; i++)
            {
                var original = jsonArray[i];
                var sanitizedItem = SanitizeJsonNode(original, sensitiveValues);
                if (!ReferenceEquals(original, sanitizedItem))
                {
                    jsonArray[i] = sanitizedItem;
                }
            }

            return jsonArray;
        }

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) && sensitiveValues.Contains(text))
        {
            return JsonValue.Create("***");
        }

        return node;
    }

    private static Dictionary<string, object> SanitizeParameters(IReadOnlyDictionary<string, object> parameters, IReadOnlySet<string> sensitiveValues)
    {
        var sanitized = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parameters)
        {
            sanitized[key] = SanitizeValue(value, sensitiveValues)!;
        }

        return sanitized;
    }

    private static object? SanitizeValue(object? value, IReadOnlySet<string> sensitiveValues)
    {
        if (value is null)
        {
            return null;
        }

        if (value is CredentialValue credential)
        {
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = credential.Name,
                ["type"] = credential.Type
            };
        }

        if (value is string text && sensitiveValues.Contains(text))
        {
            return "***";
        }

        if (value is JsonNode jsonNode)
        {
            return SanitizeJsonNode(jsonNode.DeepClone(), sensitiveValues);
        }

        if (value is IDictionary<string, object> genericDict)
        {
            var sanitizedDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, item) in genericDict)
            {
                sanitizedDict[key] = SanitizeValue(item, sensitiveValues);
            }

            return sanitizedDict;
        }

        if (value is IDictionary nonGenericDict)
        {
            var sanitizedDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in nonGenericDict)
            {
                var entryKey = entry.Key?.ToString() ?? string.Empty;
                sanitizedDict[entryKey] = SanitizeValue(entry.Value, sensitiveValues);
            }

            return sanitizedDict;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            var sanitizedList = new List<object?>();
            foreach (var item in enumerable)
            {
                sanitizedList.Add(SanitizeValue(item, sensitiveValues));
            }

            return sanitizedList;
        }

        return value;
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

    private static ExecutionDto MapToDto(ExecutionRecord record)
    {
        return new ExecutionDto
        {
            Id = record.Id,
            WorkflowDefinitionId = record.WorkflowDefinitionId,
            Status = record.Status.ToString(),
            StartedAt = record.StartedAt,
            CompletedAt = record.CompletedAt,
            NodeRecords = record.NodeRecords.Select(MapToNodeRecord).ToList()
        };
    }

    private static NodeExecutionRecordDto MapToNodeRecord(NodeExecutionRecord node)
    {
        return new NodeExecutionRecordDto
        {
            Id = node.Id,
            NodeDefinitionId = node.NodeDefinitionId,
            RunIndex = node.RunIndex,
            Status = node.Output.Success ? "Completed" : "Failed",
            StartedAt = node.StartedAt ?? default,
            CompletedAt = node.CompletedAt,
            Inputs = SerializeInputs(node.Inputs),
            Output = JsonSerializer.SerializeToNode(node.Output, JsonDefaults.Options),
            RawParameters = SerializeToDictionary(node.RawParameters),
            ResolvedParameters = SerializeToDictionary(node.ResolvedParameters)
        };
    }

    private static Dictionary<string, object>? SerializeInputs(IReadOnlyDictionary<string, DataBatch>? inputs)
    {
        if (inputs is null || inputs.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, object>(inputs.Count);
        foreach (var (key, value) in inputs)
        {
            result[key] = JsonSerializer.SerializeToNode(value, JsonDefaults.Options) ?? string.Empty;
        }

        return result;
    }

    private static Dictionary<string, object>? SerializeToDictionary(IReadOnlyDictionary<string, object>? dict)
    {
        if (dict is null || dict.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, object>(dict.Count);
        foreach (var (key, value) in dict)
        {
            result[key] = value is string or int or long or double or float or decimal or bool or DateTime
                ? value
                : JsonSerializer.SerializeToNode(value, JsonDefaults.Options) ?? string.Empty;
        }

        return result;
    }

    private sealed class DryRunWaitingArea
    {
        private readonly Dictionary<Guid, Dictionary<string, DataBatch>> _pending = new();

        public void Store(Guid nodeId, Dictionary<string, DataBatch> inputs)
        {
            _pending[nodeId] = new Dictionary<string, DataBatch>(inputs, StringComparer.OrdinalIgnoreCase);
        }

        public Dictionary<string, DataBatch> Take(Guid nodeId)
        {
            if (!_pending.TryGetValue(nodeId, out var pending))
            {
                return [];
            }

            _pending.Remove(nodeId);
            return pending;
        }
    }

    private sealed class TemporaryCredentialAccessor : ICredentialAccessor
    {
        private readonly IReadOnlyDictionary<string, CredentialValue> _credentials;

        public TemporaryCredentialAccessor(IReadOnlyDictionary<string, CredentialValue> credentials)
        {
            _credentials = credentials;
        }

        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException($"Dry-run 仅支持按名称引用临时凭据，不支持 GUID '{credentialId}'。");
        }

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            _credentials.TryGetValue(name, out var value);
            return Task.FromResult(value);
        }
    }
}
