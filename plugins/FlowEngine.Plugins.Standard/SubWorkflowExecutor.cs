using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using Microsoft.Extensions.Options;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 轻量级子工作流执行器，在当前上下文内按拓扑顺序执行节点。
/// <list type="bullet">
///   <item>多入边节点会合并所有父节点的输入（仅当全部必需输入端口就绪才执行），避免第二条入边被跳过而丢数据。</item>
///   <item>节点执行前对 Script/Expression 类型参数做预求值（复用 Core 级 <see cref="ScriptParameterPreEvaluatorCore"/>），
///   无需依赖 Runtime 层。</item>
/// </list>
/// </summary>
internal sealed class SubWorkflowExecutor
{
    private readonly INodeRegistry? _nodeRegistry;
    private readonly int _nestingDepth;

    public SubWorkflowExecutor(INodeRegistry? nodeRegistry, int nestingDepth = 0)
    {
        _nodeRegistry = nodeRegistry;
        _nestingDepth = nestingDepth;
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
        var inboundConnectionsByTarget = workflow.Connections
            .Where(c => !string.IsNullOrEmpty(c.TargetNodeId))
            .ToLookup(c => c.TargetNodeId, StringComparer.OrdinalIgnoreCase);

        var entryNodes = ResolveEntryNodes(workflow, inboundConnectionsByTarget);
        if (entryNodes.Count == 0)
        {
            return CreateErrorResult("NoEntryNode", "No entry node found in the sub-workflow.");
        }

        var nodeOutputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);
        var executed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 待合并输入：nodeId -> (port -> 已合并批次)。尚未就绪的节点不加入 executed，
        // 等待后续父节点入队时再合并执行，避免第二条入边被跳过而丢数据。
        var pendingInputs = new Dictionary<string, Dictionary<string, DataBatch>>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(entryNodes.Select(n => n.Id));
        NodeExecutionResult? lastResult = null;

        // 子工作流内复用单个 JS 引擎与脚本缓存，供 Expression 参数预求值（与运行时同生命周期）。
        using var js = JsEngine.Create();
        var scriptCache = new ScriptCache(Options.Create(new JsEngineOptions()));

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (executed.Contains(nodeId) || !nodeMap.TryGetValue(nodeId, out var node))
            {
                continue;
            }

            var nodeType = _nodeRegistry.CreateInstance(node.TypeName);

            // 入口节点（无入边）：直接用触发负载构造输入并立即执行。
            if (!inboundConnectionsByTarget.Contains(nodeId))
            {
                var entryInputs = CollectEntryInputs(node, triggerPayload);
                var entryResult = await ExecuteNodeAsync(workflow, node, nodeType, entryInputs, js, scriptCache, cancellationToken)
                    .ConfigureAwait(false);
                lastResult = entryResult;
                if (entryResult.Success)
                {
                    nodeOutputs[node.Id] = entryResult.Output;
                }

                executed.Add(nodeId);
                EnqueueOutgoing(node, nodeType, entryResult, connectionsBySource, nodeMap, executed, queue);
                if (!entryResult.Success)
                {
                    break;
                }

                continue;
            }

            // 有入边：将当前可用的父节点输出合并进 pending，并判断是否全部必需端口就绪。
            var requiredPorts = ResolveRequiredInputPorts(node, nodeType, inboundConnectionsByTarget);
            var allReady = true;
            foreach (var conn in inboundConnectionsByTarget[node.Id])
            {
                if (nodeMap.TryGetValue(conn.SourceNodeId, out var sourceNode)
                    && nodeOutputs.TryGetValue(sourceNode.Id, out var batch))
                {
                    var resolvedPort = conn.TargetPortName ?? ResolveDefaultInputPort(nodeType);
                    if (!pendingInputs.TryGetValue(nodeId, out var portMap))
                    {
                        portMap = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);
                        pendingInputs[nodeId] = portMap;
                    }

                    // 合并当前端口已累积的批次与新到达的父节点输出，重排 SourceIndex。
                    var accumulated = portMap.TryGetValue(resolvedPort, out var current) ? current : new DataBatch();
                    portMap[resolvedPort] = DataBatch.Merge(accumulated, batch);
                }
                else
                {
                    // 仍有父节点尚未产出输出：暂不执行（不加入 executed），待后续父节点入队时再合并执行。
                    allReady = false;
                }
            }

            if (!allReady)
            {
                continue;
            }

            var inputs = pendingInputs[nodeId];
            var result = await ExecuteNodeAsync(workflow, node, nodeType, inputs, js, scriptCache, cancellationToken)
                .ConfigureAwait(false);
            lastResult = result;
            if (result.Success)
            {
                nodeOutputs[node.Id] = result.Output;
            }

            executed.Add(nodeId);
            EnqueueOutgoing(node, nodeType, result, connectionsBySource, nodeMap, executed, queue);
            if (!result.Success)
            {
                break;
            }
        }

        return lastResult ?? CreateErrorResult("NoResult", "Sub-workflow produced no result.");
    }

    private async Task<NodeExecutionResult> ExecuteNodeAsync(
        Workflow workflow,
        NodeDefinition node,
        INodeType nodeType,
        Dictionary<string, DataBatch> inputs,
        JsEngine js,
        ScriptCache scriptCache,
        CancellationToken cancellationToken)
    {
        var context = BuildNodeContext(workflow, node, inputs, cancellationToken, _nestingDepth);

        // 执行前对 Script/Expression 参数做预求值（复用 Core 级实现，插件仅依赖 Core）。
        var rawParameters = new Dictionary<string, object>(node.Parameters, StringComparer.OrdinalIgnoreCase);
        if (_nodeRegistry is not null)
        {
            var descriptor = _nodeRegistry.GetDescriptor(node.TypeName);
            var preEvalContext = new ScriptContext(context, context.GlobalVariables);
            await ScriptParameterPreEvaluatorCore.PreEvaluateAsync(rawParameters, descriptor, preEvalContext, js, scriptCache, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            HydrateParameters(nodeType, rawParameters);
            context.RawParameters = rawParameters;
            context.ResolvedParameters = rawParameters;
            return await nodeType.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return CreateExceptionResult(ex, node.Id);
        }
    }

    private static List<NodeDefinition> ResolveEntryNodes(Workflow workflow, ILookup<string, Connection> inboundConnectionsByTarget)
    {
        return workflow.Nodes
            .Where(n => n.IsEntry || !inboundConnectionsByTarget.Contains(n.Id))
            .ToList();
    }

    private static Dictionary<string, DataBatch> CollectEntryInputs(NodeDefinition node, JsonNode? triggerPayload)
    {
        var inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);
        if (triggerPayload is not null)
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

    private static IReadOnlyList<string> ResolveRequiredInputPorts(
        NodeDefinition node,
        INodeType nodeType,
        ILookup<string, Connection> inboundConnectionsByTarget)
    {
        var ports = new List<string>();
        foreach (var conn in inboundConnectionsByTarget[node.Id])
        {
            var resolved = conn.TargetPortName ?? ResolveDefaultInputPort(nodeType);
            if (!ports.Contains(resolved, StringComparer.OrdinalIgnoreCase))
            {
                ports.Add(resolved);
            }
        }

        return ports;
    }

    private static string ResolveDefaultInputPort(INodeType nodeType)
    {
        var inputPorts = nodeType.Ports
            .Where(p => p.Direction == PortDirection.Input)
            .ToList();
        return inputPorts.Count > 0 ? inputPorts[0].Name : FlowConstants.PortNames.Input;
    }

    private static NodeExecutionContext BuildNodeContext(
        Workflow workflow,
        NodeDefinition node,
        Dictionary<string, DataBatch> inputs,
        CancellationToken cancellationToken,
        int nestingDepth)
    {
        return new NodeExecutionContext
        {
            Workflow = workflow,
            ExecutionId = Guid.NewGuid(),
            NestingDepth = nestingDepth,
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
