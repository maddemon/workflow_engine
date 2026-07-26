using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Agent;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Dtos;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 子 Agent 工具节点，作为 Agent 的工具调用嵌套执行另一个 Agent。
/// </summary>
[NodeMeta(TypeName = "subAgentTool", DisplayName = "Sub-Agent Tool", Category = NodeCategory.AI, Icon = "robot", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
[Port(FlowConstants.PortNames.Tools, "Tool (optional)", PortDirection.Input, PortType.AgentTool)]
[Port(FlowConstants.PortNames.Llm, "LLM (required - connect an LLM provider node)", PortDirection.Input, PortType.LLM)]
public sealed class SubAgentToolNode : NodeBase
{
    private const int DefaultMaxNestingDepth = 3;
    private const int DefaultMaxIterations = 10;
    private const int MinMaxNestingDepth = 1;
    private const int MaxMaxNestingDepth = 10;
    private const int MinMaxIterations = 1;
    private const int MaxMaxIterations = 100;
    private const int DefaultMemoryWindowSize = 20;

    /// <summary>
    /// 嵌套 Agent 的系统提示词。
    /// </summary>
    [Description("System prompt for the nested agent.")]
    public string PromptTemplate { get; set; } = string.Empty;

    /// <summary>
    /// 最大嵌套深度。
    /// </summary>
    [Description("Maximum nesting depth to prevent infinite recursion.")]
    public int MaxNestingDepth { get; set; } = DefaultMaxNestingDepth;

    /// <summary>
    /// 是否启用对话记忆。
    /// </summary>
    [Description("Enable conversation memory across iterations.")]
    public bool MemoryEnabled { get; set; }

    /// <summary>
    /// 记忆窗口大小（保留最近 N 条消息）。
    /// </summary>
    [Description("Number of recent messages to keep in memory window.")]
    public int MemoryWindowSize { get; set; } = DefaultMemoryWindowSize;

    /// <inheritdoc />
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        var effectiveMaxNestingDepth = ResolveMaxNestingDepth();

        if (NestingLevel >= effectiveMaxNestingDepth)
        {
            throw new NodeExecutionException(
                "MaxNestingDepthExceeded",
                $"Agent nesting depth {NestingLevel} exceeds maximum allowed depth of {effectiveMaxNestingDepth}.");
        }

        if (LlmClient is null)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.MissingLlmClient, "LLM client not available for nested agent.");
        }

        var tools = CollectTools();
        var messages = BuildMessages(input);
        var maxIterations = ResolveMaxIterations();
        var memoryEnabled = ResolveMemoryEnabled();
        var memoryWindowSize = ResolveMemoryWindowSize();

        var parentRecordId = ExecutionContext.NodeExecutionRecordId != Guid.Empty
            ? ExecutionContext.NodeExecutionRecordId
            : ExecutionContext.ExecutionId;

        AgentMemory? memory = null;
        if (memoryEnabled)
        {
            memory = new AgentMemory(memoryWindowSize > 0 ? memoryWindowSize : DefaultMemoryWindowSize);
        }

        var resolver = new Core.Agent.InlineResolver(
            LlmClient,
            tools,
            ExecutionContext,
            maxIterations,
            parentRecordId: parentRecordId,
            memory: memory,
            logger: Logger);

        try
        {
            var result = await resolver.RunAsync(messages, ct).ConfigureAwait(false);

            switch (result.StoppedReason)
            {
                case Core.Agent.InlineResolverStopReason.Completed:
                    {
                        var dto = new AgentExecutionResultDto
                        {
                            AgentInfo = new AgentExecutionInfoDto
                            {
                                Model = LlmClient?.ModelName ?? "unknown",
                                IterationCount = result.Iterations.Count,
                                Status = "Completed",
                                StartedAt = null,
                                CompletedAt = DateTime.UtcNow,
                                ErrorMessage = null,
                                TokenUsage = null,
                            },
                            Iterations = result.Iterations,
                            SubRecords = new List<SubRecordDto>(),
                            SystemPrompt = null,
                        };

                        return NodeHandlerOutput.Data(new DataBatch
                        {
                            Items =
                            [
                                new DataItem
                                {
                                    Data = JsonSerializer.SerializeToNode(dto, JsonDefaults.Options),
                                    Success = true,
                                    SourceIndex = 0,
                                }
                            ]
                        });
                    }

                case Core.Agent.InlineResolverStopReason.MaxIterationsReached:
                    return NodeHandlerOutput.Failure(
                        FlowConstants.ErrorCodes.Cancelled,
                        $"Sub-Agent reached maximum iterations ({maxIterations}).",
                        BuildFailureBatch(result),
                        ExecutionContext.Node?.Id);

                case Core.Agent.InlineResolverStopReason.Cancelled:
                    return NodeHandlerOutput.Failure(
                        FlowConstants.ErrorCodes.Cancelled,
                        "Sub-Agent execution was cancelled.",
                        BuildFailureBatch(result),
                        ExecutionContext.Node?.Id);

                default:
                    return NodeHandlerOutput.Failure(
                        FlowConstants.ErrorCodes.Cancelled,
                        "Sub-Agent execution stopped.",
                        BuildFailureBatch(result),
                        ExecutionContext.Node?.Id);
            }
        }
        catch (OperationCanceledException)
        {
            return NodeHandlerOutput.Failure(
                FlowConstants.ErrorCodes.Cancelled,
                "Sub-Agent execution was cancelled.",
                null,
                ExecutionContext.Node?.Id);
        }
        catch (Exception ex)
        {
            return NodeHandlerOutput.Failure(
                "LlmError",
                $"Sub-Agent LLM call failed: {ex.Message}",
                null,
                ExecutionContext.Node?.Id);
        }
    }

    /// <summary>
    /// 构造失败时的输出批次（携带结果 DTO，成功标志为 true），失败语义由 <see cref="NodeHandlerOutput.Error"/> 表达。
    /// </summary>
    private static DataBatch BuildFailureBatch(Core.Agent.InlineResolverResult result)
    {
        var dto = new AgentExecutionResultDto
        {
            AgentInfo = new AgentExecutionInfoDto
            {
                Model = "unknown",
                IterationCount = result.Iterations.Count,
                Status = "Failed",
                StartedAt = null,
                CompletedAt = DateTime.UtcNow,
                ErrorMessage = null,
                TokenUsage = null,
            },
            Iterations = result.Iterations,
            SubRecords = new List<SubRecordDto>(),
            SystemPrompt = null,
        };

        return new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = JsonSerializer.SerializeToNode(dto, JsonDefaults.Options),
                    Success = true,
                    SourceIndex = 0,
                }
            ]
        };
    }

    /// <summary>
    /// 从节点参数解析最大嵌套深度，未配置时使用属性默认值，并校验范围 [1, 10]。
    /// </summary>
    private int ResolveMaxNestingDepth()
    {
        var depth = CoerceInt(GetResolvedParameter("maxNestingDepth"));
        if (depth.HasValue)
        {
            if (depth.Value < MinMaxNestingDepth) return MinMaxNestingDepth;
            if (depth.Value > MaxMaxNestingDepth) return MaxMaxNestingDepth;
            return depth.Value;
        }

        return MaxNestingDepth;
    }

    /// <summary>
    /// 从节点参数解析最大 LLM 迭代次数，未配置时使用默认值，并校验范围 [1, 100]。
    /// </summary>
    private int ResolveMaxIterations()
    {
        var iterations = CoerceInt(GetResolvedParameter("maxIterations"));
        if (iterations.HasValue)
        {
            if (iterations.Value < MinMaxIterations) return MinMaxIterations;
            if (iterations.Value > MaxMaxIterations) return MaxMaxIterations;
            return iterations.Value;
        }

        return DefaultMaxIterations;
    }

    /// <summary>
    /// 从节点参数解析是否启用记忆，未配置时使用属性默认值。
    /// </summary>
    private bool ResolveMemoryEnabled()
    {
        var enabled = GetResolvedParameter("memoryEnabled");
        if (enabled is bool b) return b;
        if (enabled is JsonValue jv && jv.TryGetValue<bool>(out var jb)) return jb;

        return MemoryEnabled;
    }

    /// <summary>
    /// 从节点参数解析记忆窗口大小，未配置时使用属性默认值，最小为 1。
    /// </summary>
    private int ResolveMemoryWindowSize()
    {
        var size = CoerceInt(GetResolvedParameter("memoryWindowSize"));
        if (size.HasValue) return Math.Max(1, size.Value);

        return Math.Max(1, MemoryWindowSize);
    }

    /// <summary>
    /// 将可能的 JsonValue 或已解析的 CLR 值安全转换为 int。
    /// </summary>
    private static int? CoerceInt(object? raw)
    {
        if (raw is int i) return i;
        if (raw is JsonValue jv && jv.TryGetValue<int>(out var ji)) return ji;
        return null;
    }

    private IReadOnlyList<ToolDefinition> CollectTools()
    {
        var workflow = ExecutionContext.Workflow;
        var currentNodeId = ExecutionContext.Node.Id;

        var toolConnections = workflow.Connections
            .Where(c => c.TargetNodeId == currentNodeId && c.TargetPortName == FlowConstants.PortNames.Tools)
            .ToList();

        if (toolConnections.Count == 0)
        {
            return [];
        }

        var tools = new List<ToolDefinition>();
        foreach (var connection in toolConnections)
        {
            var toolNode = workflow.Nodes.FirstOrDefault(n => n.Id == connection.SourceNodeId);
            if (toolNode is null)
            {
                continue;
            }

            INodeType? nodeType = null;
            if (Registry?.TryGet(toolNode.TypeName, out var resolvedType) == true)
            {
                nodeType = resolvedType;
            }

            if (nodeType is null)
            {
                continue;
            }

            NodeTypeDescriptor? descriptor = null;
            try
            {
                descriptor = Registry?.GetDescriptor(toolNode.TypeName);
            }
            catch (InvalidOperationException)
            {
                // Descriptor not found, skip
            }

            var parametersSchema = Core.Tools.SchemaDerivation.DeriveSchema(descriptor?.Parameters);

            tools.Add(new ToolDefinition
            {
                Name = toolNode.Name,
                Description = AgentToolDescriptionHelper.ResolveToolDescription(nodeType, descriptor),
                TargetNodeDefinitionId = toolNode.Id,
                ParametersSchema = parametersSchema
            });
        }

        return tools;
    }

    private List<LlmMessage> BuildMessages(NodeInput input)
    {
        var messages = new List<LlmMessage>();

        if (!string.IsNullOrWhiteSpace(PromptTemplate))
        {
            messages.Add(new LlmMessage { Role = "system", Content = PromptTemplate });
        }

        var batch = input.InputBatch;
        if (batch.Items.Count > 0)
        {
            if (batch.Items.Count > 1)
            {
                Logger?.LogWarning("Sub-Agent 节点收到 {Count} 条输入数据，仅处理第一条，其余将被忽略。", batch.Items.Count);
            }

            var firstItem = batch.Items[0];
            if (firstItem.Data is not null)
            {
                messages.Add(new LlmMessage { Role = "user", Content = firstItem.Data.ToJsonString() });
            }
        }

        return messages;
    }
}
