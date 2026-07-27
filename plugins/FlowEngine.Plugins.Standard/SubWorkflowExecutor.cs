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
    private readonly IExecutionLogger? _logger;

    public SubWorkflowExecutor(INodeRegistry? nodeRegistry, int nestingDepth = 0, IExecutionLogger? logger = null)
    {
        _nodeRegistry = nodeRegistry;
        _nestingDepth = nestingDepth;
        _logger = logger;
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
        // 与主运行时 ExecutionSession 一致：连接 lookup 的 SourcePortName 为空时，解析为源节点的首个
        // 输出端口名（先取节点显式 Ports，回退到注册中心描述符），并归一化为小写，避免 If/Switch 空端口名连接命中不到下游。
        var connectionsBySource = workflow.Connections
            .ToLookup(c => (c.SourceNodeId, ResolveLookupPortName(c, nodeMap).ToLowerInvariant()));
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
            HydrateParameters(nodeType, rawParameters, _logger, node.Id);
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
        var outgoingConnections = connectionsBySource[(node.Id, sourcePortName.ToLowerInvariant())];

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
    // ParameterHydrator。此处保留精简版属性注入，显式处理数值/布尔/字符串/枚举/Json 等目标类型，
    // 委托 Core 层 ScriptValueConverter 处理 Script/Dictionary<string,Script>，避免与 Runtime 重复实现。
    private static void HydrateParameters(INodeType nodeType, Dictionary<string, object> parameters, IExecutionLogger? logger, string nodeId)
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

            // 显式、正确的转换：不再用 catch{} 静默吞掉失败。转换失败时记录警告并向上抛出，
            // 由 ExecuteNodeAsync 兜底为失败结果，避免对“存在的值”静默产出 null。
            try
            {
                var converted = ConvertParameterValue(value, property.PropertyType);

                // 跳过非可空值类型赋 null（避免默认值覆盖），其余一律写入（含可空值类型赋 null）。
                if (converted is null && property.PropertyType.IsValueType
                    && Nullable.GetUnderlyingType(property.PropertyType) is null)
                {
                    continue;
                }

                property.SetValue(nodeType, converted);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    "SubWorkflow: 节点 {NodeId} 参数 {Param} 转换失败: {Error}；值类型 {ValueType} -> 目标类型 {TargetType}",
                    nodeId, property.Name, ex.Message, value?.GetType().Name, property.PropertyType.Name);
                throw;
            }
        }
    }

    /// <summary>
    /// 将已解析参数值（可能为 <see cref="JsonElement"/> 或普通 CLR 值）显式转换为目标属性类型。
    /// 数值按 <see cref="ParameterResolver.ResolveNumber"/> 的意图优先 long/decimal，避免精度丢失；
    /// 不再静默吞掉失败——无法转换的“存在的值”将抛出异常，由调用方记录并向上传播。
    /// </summary>
    /// <param name="value">待转换的值（可能为 null，仅当调用方已确认非 null 时进入类型分支）。</param>
    /// <param name="targetType">目标属性声明类型（可能为可空值类型）。</param>
    /// <returns>转换后的对象；值本身为 null 时返回 null。</returns>
    private static object? ConvertParameterValue(object? value, Type targetType)
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
            return ConvertToString(value);
        }

        if (underlying == typeof(bool))
        {
            return ConvertToBool(value);
        }

        if (underlying == typeof(Guid))
        {
            return ConvertToGuid(value);
        }

        if (underlying == typeof(DateTime))
        {
            return ConvertToDateTime(value);
        }

        if (underlying.IsEnum)
        {
            return ConvertToEnum(value, underlying);
        }

        if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(decimal)
            || underlying == typeof(double) || underlying == typeof(float))
        {
            return ConvertToNumber(value, underlying);
        }

        if (underlying == typeof(JsonObject) || underlying == typeof(JsonNode))
        {
            return ConvertToJson(value);
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

        // 兜底：标准类型转换；失败（类型不兼容）直接抛出，不再返回 null。
        try
        {
            return Convert.ChangeType(value, underlying);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"无法将值 {value}（{value.GetType().Name}）转换为 {underlying.Name}。", ex);
        }
    }

    private static string ConvertToString(object value)
        => value is JsonElement je
            ? je.ValueKind == JsonValueKind.String ? je.GetString() ?? string.Empty : je.GetRawText()
            : value.ToString() ?? string.Empty;

    private static object ConvertToBool(object value)
    {
        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (je.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (je.ValueKind == JsonValueKind.String && bool.TryParse(je.GetString(), out var parsed))
            {
                return parsed;
            }

            throw new InvalidOperationException($"无法将 JSON 值（{je.ValueKind}）转换为 bool。");
        }

        if (value is bool b)
        {
            return b;
        }

        if (value is string s && bool.TryParse(s, out var parsedString))
        {
            return parsedString;
        }

        throw new InvalidOperationException($"无法将值 {value}（{value.GetType().Name}）转换为 bool。");
    }

    private static object ConvertToGuid(object value)
    {
        if (value is JsonElement je && je.ValueKind == JsonValueKind.String && Guid.TryParse(je.GetString(), out var fromJson))
        {
            return fromJson;
        }

        if (value is Guid g)
        {
            return g;
        }

        if (value is string s && Guid.TryParse(s, out var fromString))
        {
            return fromString;
        }

        throw new InvalidOperationException($"无法将值 {value}（{value.GetType().Name}）转换为 Guid。");
    }

    private static object ConvertToDateTime(object value)
    {
        if (value is JsonElement je && je.ValueKind == JsonValueKind.String && DateTime.TryParse(je.GetString(), out var fromJson))
        {
            return fromJson;
        }

        if (value is DateTime dt)
        {
            return dt;
        }

        if (value is string s && DateTime.TryParse(s, out var fromString))
        {
            return fromString;
        }

        throw new InvalidOperationException($"无法将值 {value}（{value.GetType().Name}）转换为 DateTime。");
    }

    private static object ConvertToEnum(object value, Type underlying)
    {
        if (value is JsonElement je && je.ValueKind == JsonValueKind.String
            && Enum.TryParse(underlying, je.GetString(), ignoreCase: true, out var fromJson))
        {
            return fromJson;
        }

        if (value is string s && Enum.TryParse(underlying, s, ignoreCase: true, out var fromString))
        {
            return fromString;
        }

        if (value is int or long or double or decimal)
        {
            return Enum.ToObject(underlying, value);
        }

        throw new InvalidOperationException($"无法将值 {value}（{value.GetType().Name}）转换为枚举 {underlying.Name}。");
    }

    private static object ConvertToNumber(object value, Type underlying)
    {
        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number)
            {
                // 整数优先 long，其次 decimal，最后 double，再转换到目标数值类型（与 ParameterResolver.ResolveNumber 一致）。
                object number = je.TryGetInt64(out var l)
                    ? l
                    : je.TryGetDecimal(out var d)
                        ? (object)d
                        : je.GetDouble();
                return ConvertNumericValue(number, underlying);
            }

            if (je.ValueKind == JsonValueKind.String && double.TryParse(je.GetString(), out var fromString))
            {
                return ConvertNumericValue(fromString, underlying);
            }

            throw new InvalidOperationException($"无法将 JSON 值（{je.ValueKind}）转换为数值 {underlying.Name}。");
        }

        return ConvertNumericValue(value, underlying);
    }

    private static object? ConvertToJson(object value)
    {
        return value switch
        {
            JsonObject obj => obj,
            JsonNode node => node as JsonObject,
            JsonElement je when je.ValueKind == JsonValueKind.Object => JsonNode.Parse(je.GetRawText()) as JsonObject,
            string s => JsonNode.Parse(s) as JsonObject,
            _ => throw new InvalidOperationException($"无法将值 {value}（{value.GetType().Name}）转换为 JsonObject。")
        };
    }

    private static object ConvertNumericValue(object value, Type underlying)
    {
        if (underlying == typeof(long))
        {
            return Convert.ToInt64(value);
        }

        if (underlying == typeof(int))
        {
            return Convert.ToInt32(value);
        }

        if (underlying == typeof(decimal))
        {
            return Convert.ToDecimal(value);
        }

        if (underlying == typeof(double))
        {
            return Convert.ToDouble(value);
        }

        if (underlying == typeof(float))
        {
            return Convert.ToSingle(value);
        }

        throw new InvalidOperationException($"不支持的数值目标类型 {underlying.Name}。");
    }

    /// <summary>
    /// 解析连接 lookup 的源端口名：与主运行时 <c>ExecutionSession</c> 一致，
    /// 空 <see cref="Connection.SourcePortName"/> 解析为源节点的首个输出端口名
    /// （先取节点显式 Ports，回退到注册中心描述符），修复 If/Switch 空端口名连接无法命中下游的问题。
    /// </summary>
    /// <param name="connection">连接。</param>
    /// <param name="nodeMap">节点字典（按 Id）。</param>
    /// <returns>解析后的源端口名（空字符串表示仍无法解析）。</returns>
    private string ResolveLookupPortName(Connection connection, Dictionary<string, NodeDefinition> nodeMap)
    {
        var portName = connection.SourcePortName;
        if (string.IsNullOrEmpty(portName) && nodeMap.TryGetValue(connection.SourceNodeId, out var sourceNode))
        {
            portName = sourceNode.Ports.FirstOrDefault(p => p.Direction == PortDirection.Output)?.Name ?? string.Empty;

            if (string.IsNullOrEmpty(portName) && _nodeRegistry is not null)
            {
                try
                {
                    var descriptor = _nodeRegistry.GetDescriptor(sourceNode.TypeName);
                    portName = descriptor.Ports.FirstOrDefault(p => p.Direction == PortDirection.Output)?.Name ?? string.Empty;
                }
                catch (Exception ex)
                {
                    // 注册中心查找失败，保持空端口名（保底回退）；记录以便排查端口解析问题。
                    _logger?.LogWarning("解析源端口名时注册中心查找失败，回退为空端口名。节点类型={TypeName}, 错误={Error}", sourceNode.TypeName, ex.Message);
                }
            }
        }

        return portName ?? string.Empty;
    }
}
