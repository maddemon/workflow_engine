using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;

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
            .ToLookup(c => (c.SourceNodeId, c.SourcePortName ?? string.Empty));
        var hasInputConnections = workflow.Connections
            .Select(c => c.TargetNodeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var entryNodes = ResolveEntryNodes(workflow, hasInputConnections);
        if (entryNodes.Count == 0)
        {
            return CreateErrorResult("NoEntryNode", "No entry node found in the sub-workflow.");
        }

        var nodeOutputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);
        var executed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(entryNodes.Select(n => n.Id));
        NodeExecutionResult? lastResult = null;

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (executed.Contains(nodeId) || !nodeMap.TryGetValue(nodeId, out var node))
            {
                continue;
            }

            var nodeType = _nodeRegistry.Get(node.TypeName);
            var inputs = CollectInputs(node, workflow, nodeMap, nodeOutputs, entryNodes, triggerPayload);
            var context = BuildNodeContext(workflow, node, inputs, cancellationToken);

            NodeExecutionResult result;
            try
            {
                HydrateParameters(nodeType, node.Parameters);
                result = await nodeType.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = CreateExceptionResult(ex, node.Id);
            }

            executed.Add(nodeId);
            lastResult = result;

            if (result.Success)
            {
                nodeOutputs[node.Name] = result.Output;
            }

            EnqueueOutgoing(node, nodeType, result, connectionsBySource, nodeMap, executed, queue);

            if (!result.Success)
            {
                break;
            }
        }

        return lastResult ?? CreateErrorResult("NoResult", "Sub-workflow produced no result.");
    }

    private static List<NodeDefinition> ResolveEntryNodes(Workflow workflow, HashSet<string> hasInputConnections)
    {
        return workflow.Nodes
            .Where(n => n.IsEntry || !hasInputConnections.Contains(n.Id))
            .ToList();
    }

    private static Dictionary<string, DataBatch> CollectInputs(
        NodeDefinition node,
        Workflow workflow,
        Dictionary<string, NodeDefinition> nodeMap,
        Dictionary<string, DataBatch> nodeOutputs,
        List<NodeDefinition> entryNodes,
        JsonNode? triggerPayload)
    {
        var inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);

        var incomingConnections = workflow.Connections
            .Where(c => c.TargetNodeId == node.Id)
            .ToList();

        if (incomingConnections.Count > 0)
        {
            foreach (var conn in incomingConnections)
            {
                if (nodeMap.TryGetValue(conn.SourceNodeId, out var sourceNode)
                    && nodeOutputs.TryGetValue(sourceNode.Name, out var batch))
                {
                    var resolvedPort = conn.TargetPortName ?? FlowConstants.PortNames.Input;
                    inputs[resolvedPort] = batch;
                }
            }
        }
        else if (entryNodes.Any(n => n.Id == node.Id) && triggerPayload is not null)
        {
            inputs[FlowConstants.PortNames.Input] = new DataBatch
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

        return inputs;
    }

    private static NodeExecutionContext BuildNodeContext(
        Workflow workflow,
        NodeDefinition node,
        Dictionary<string, DataBatch> inputs,
        CancellationToken cancellationToken)
    {
        return new NodeExecutionContext
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
    }

    private static void EnqueueOutgoing(
        NodeDefinition node,
        INodeType nodeType,
        NodeExecutionResult result,
        ILookup<(string SourceNodeId, string SourcePortName), Connection> connectionsBySource,
        Dictionary<string, NodeDefinition> nodeMap,
        HashSet<string> executed,
        Queue<string> queue)
    {
        var sourcePortName = ResolveSourcePortName(nodeType, result);
        var outgoingConnections = connectionsBySource[(node.Id, sourcePortName)];

        foreach (var conn in outgoingConnections)
        {
            if (nodeMap.ContainsKey(conn.TargetNodeId) && !executed.Contains(conn.TargetNodeId))
            {
                queue.Enqueue(conn.TargetNodeId);
            }
        }
    }

    private static NodeExecutionResult CreateExceptionResult(Exception ex, string nodeId)
    {
        return new NodeExecutionResult
        {
            Success = false,
            Error = new NodeError
            {
                Code = ex.GetType().Name,
                Message = ex.Message,
                NodeDefinitionId = nodeId
            }
        };
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

        return FlowConstants.PortNames.Output;
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

    // 注：FlowEngine.Plugins.Standard 仅引用 FlowEngine.Core，无法注入 Runtime 层的
    // ParameterHydrator。此处保留精简版属性注入，Script/Dictionary<string,Script> 已
    // 委托给 Core 层 ScriptValueConverter，避免与 Runtime 重复实现转换逻辑。
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

            // R12：属性名可能为空或单字符，避免 Name[0]/Name[1..] 越界（如编译生成的占位属性）。
            if (property.Name.Length == 0)
            {
                continue;
            }

            var camelName = string.Concat(
                char.ToLowerInvariant(property.Name[0]),
                property.Name.Length > 1 ? property.Name[1..] : string.Empty);
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

        if (underlying == typeof(Script))
        {
            return ScriptValueConverter.ToScript(value);
        }

        if (underlying.IsGenericType
            && underlying.GetGenericTypeDefinition() == typeof(Dictionary<,>)
            && underlying.GetGenericArguments() is Type[] args
            && args.Length == 2
            && args[0] == typeof(string)
            && args[1] == typeof(Script))
        {
            return ScriptValueConverter.TryGetScriptDictionary(value, out var dict) ? dict : null;
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
