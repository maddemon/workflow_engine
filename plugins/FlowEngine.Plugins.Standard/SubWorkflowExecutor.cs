using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 轻量级子工作流执行器，在当前上下文内按拓扑顺序执行节点。
/// </summary>
internal sealed class SubWorkflowExecutor
{
    private readonly INodeRegistry? _nodeRegistry;

    public SubWorkflowExecutor(INodeRegistry? nodeRegistry)
    {
        _nodeRegistry = nodeRegistry;
    }

    public async Task<NodeExecutionResult> ExecuteAsync(
        Workflow workflow,
        JsonNode? triggerPayload,
        CancellationToken cancellationToken)
    {
        if (_nodeRegistry is null)
        {
            return CreateErrorResult("NoNodeRegistry", "Node registry is not available.");
        }

        var nodeMap = workflow.Nodes.ToDictionary(n => n.Id);
        var connectionsBySource = workflow.Connections
            .ToLookup(c => (c.SourceNodeId, c.SourcePortName));

        var hasInputConnections = workflow.Connections
            .Select(c => c.TargetNodeId)
            .ToHashSet();

        var nodeOutputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);

        var entryNodes = workflow.Nodes
            .Where(n => n.IsEntry || !hasInputConnections.Contains(n.Id))
            .ToList();

        if (entryNodes.Count == 0)
        {
            return CreateErrorResult("NoEntryNode", "No entry node found in the sub-workflow.");
        }

        NodeExecutionResult? lastResult = null;

        var executed = new HashSet<Guid>();
        var queue = new Queue<Guid>(entryNodes.Select(n => n.Id));

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (executed.Contains(nodeId))
            {
                continue;
            }

            if (!nodeMap.TryGetValue(nodeId, out var node))
            {
                continue;
            }

            var nodeType = _nodeRegistry.Get(node.TypeName);

            var inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);

            var incomingConnections = workflow.Connections
                .Where(c => c.TargetNodeId == nodeId)
                .ToList();

            if (incomingConnections.Count > 0)
            {
                foreach (var conn in incomingConnections)
                {
                    if (nodeMap.TryGetValue(conn.SourceNodeId, out var sourceNode)
                        && nodeOutputs.TryGetValue(sourceNode.Name, out var batch))
                    {
                        inputs[conn.TargetPortName] = batch;
                    }
                }
            }
            else if (entryNodes.Any(n => n.Id == nodeId) && triggerPayload is not null)
            {
                inputs["input"] = new DataBatch
                {
                    Items =
                    [
                        new DataItem
                        {
                            Data = triggerPayload,
                            Success = true,
                            SourceIndex = 0
                        }
                    ]
                };
            }

            var context = new NodeExecutionContext
            {
                Workflow = workflow,
                ExecutionId = Guid.NewGuid(),
                Node = new NodeDefinition
                {
                    Id = node.Id,
                    TypeName = node.TypeName,
                    Name = node.Name,
                    Parameters = node.Parameters,
                    Ports = node.Ports
                },
                Inputs = inputs,
                RawParameters = node.Parameters,
                ResolvedParameters = node.Parameters,
                CancellationToken = cancellationToken
            };

            NodeExecutionResult result;
            try
            {
                HydrateParameters(nodeType, node.Parameters);
                result = await nodeType.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = new NodeExecutionResult
                {
                    Success = false,
                    Error = new NodeError
                    {
                        Code = ex.GetType().Name,
                        Message = ex.Message,
                        NodeDefinitionId = node.Id
                    }
                };
            }

            executed.Add(nodeId);
            lastResult = result;

            if (result.Success)
            {
                nodeOutputs[node.Name] = result.Output;
            }

            var sourcePortName = ResolveSourcePortName(nodeType, result);
            var outgoingConnections = connectionsBySource[(node.Id, sourcePortName)];

            foreach (var conn in outgoingConnections)
            {
                if (nodeMap.ContainsKey(conn.TargetNodeId) && !executed.Contains(conn.TargetNodeId))
                {
                    queue.Enqueue(conn.TargetNodeId);
                }
            }

            if (!result.Success)
            {
                break;
            }
        }

        return lastResult ?? CreateErrorResult("NoResult", "Sub-workflow produced no result.");
    }

    private static string ResolveSourcePortName(INodeType nodeType, NodeExecutionResult result)
    {
        if (result.BranchIndex.HasValue)
        {
            var outputPorts = nodeType.Ports
                .Where(p => p.Direction == PortDirection.Output)
                .ToList();

            var index = result.BranchIndex.Value;
            if (index >= 0 && index < outputPorts.Count)
            {
                return outputPorts[index].Name;
            }
        }

        return "output";
    }

    private static NodeExecutionResult CreateErrorResult(string code, string message)
    {
        return new NodeExecutionResult
        {
            Success = false,
            Error = new NodeError
            {
                Code = code,
                Message = message
            }
        };
    }

    private static void HydrateParameters(INodeType nodeType, Dictionary<string, object> parameters)
    {
        var type = nodeType.GetType();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod is null || property.GetMethod is null)
            {
                continue;
            }

            if (property.Name == nameof(INodeType.Ports))
            {
                continue;
            }

            if (property.DeclaringType == typeof(INodeType))
            {
                continue;
            }

            var camelName = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
            if (!parameters.TryGetValue(camelName, out var value))
            {
                continue;
            }

            try
            {
                var converted = ConvertParameterValue(value, property.PropertyType);
                if (converted is not null || Nullable.GetUnderlyingType(property.PropertyType) is not null)
                {
                    property.SetValue(nodeType, converted);
                }
            }
            catch
            {
                // Skip failed conversions
            }
        }
    }

    private static object? ConvertParameterValue(object value, Type targetType)
    {
        if (value is null)
        {
            return null;
        }

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying.IsAssignableFrom(value.GetType()))
        {
            return value;
        }

        if (underlying == typeof(string))
        {
            return value.ToString();
        }

        if (underlying == typeof(int) && value is double d)
        {
            return (int)d;
        }

        if (underlying == typeof(bool) && value is JsonElement boolElement)
        {
            return boolElement.ValueKind == JsonValueKind.True;
        }

        if (underlying == typeof(JsonObject))
        {
            return value switch
            {
                JsonObject obj => obj,
                JsonNode node => node as JsonObject,
                string s => JsonNode.Parse(s) as JsonObject,
                _ => null
            };
        }

        try
        {
            return Convert.ChangeType(value, underlying);
        }
        catch
        {
            return null;
        }
    }
}
