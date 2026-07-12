using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Dtos;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Agent;
using FlowEngine.Core.Tools;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// AI Agent 节点，通过 LLM 循环调用下游工具节点完成任务。
/// </summary>
public sealed class AgentNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "agent";

    /// <inheritdoc />
    public string DisplayName => "Agent";

    /// <inheritdoc />
    public string Category => "AI";

    /// <inheritdoc />
    public string Icon => "bot";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 最大 LLM 迭代次数。
    /// </summary>
    [Description("Maximum number of LLM iterations before forced termination.")]
    public int MaxIterations { get; set; } = 10;

    /// <summary>
    /// LLM 调用超时时间（秒）。
    /// </summary>
    [Description("LLM call timeout in seconds. Empty means no timeout.")]
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// 系统提示词模板。
    /// </summary>
    [Description("System prompt template for the LLM.")]
    public string PromptTemplate { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用对话记忆。
    /// </summary>
    [Description("Enable conversation memory across iterations.")]
    public bool MemoryEnabled { get; set; }

    /// <summary>
    /// 记忆窗口大小（保留最近 N 条消息）。
    /// </summary>
    [Description("Number of recent messages to keep in memory window.")]
    public int MemoryWindowSize { get; set; } = 20;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Tools, DisplayName = "Tool", Direction = PortDirection.Input, Type = PortType.AgentTool },
        new PortDefinition { Name = FlowConstants.PortNames.Llm, DisplayName = "LLM", Direction = PortDirection.Input, Type = PortType.LLM }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var llmClient = context.LlmClient;
        if (llmClient is null)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.MissingLlmClient, "LLM client not available. Connect an LLM supply node.");
        }

        var tools = CollectTools(context);
        var messages = BuildMessages(context);

        var maxIterations = MaxIterations > 0 ? MaxIterations : 10;
        using var timeoutCts = TimeoutSeconds.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;

        if (timeoutCts is not null && TimeoutSeconds.HasValue)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds.Value));
        }

        var effectiveToken = timeoutCts?.Token ?? cancellationToken;

        AgentMemory? memory = null;
        if (MemoryEnabled)
        {
            memory = new AgentMemory(MemoryWindowSize > 0 ? MemoryWindowSize : 20);
        }

        var resolver = new InlineResolver(
            llmClient,
            tools,
            context,
            maxIterations,
            memory: memory);

        try
        {
            var result = await resolver.RunAsync(messages, effectiveToken).ConfigureAwait(false);

            switch (result.StoppedReason)
            {
                case InlineResolverStopReason.Completed:
                    return CreateSuccessResult(result, context);

                case InlineResolverStopReason.MaxIterationsReached:
                    return CreateTimeoutResult($"Maximum iterations ({maxIterations}) reached.", context);

                case InlineResolverStopReason.Cancelled:
                    return CreateTimeoutResult("LLM call timed out or was cancelled.", context);

                default:
                    return CreateTimeoutResult("Agent execution stopped.", context);
            }
        }
        catch (OperationCanceledException) when (timeoutCts is not null)
        {
            return CreateTimeoutResult("LLM call timed out.", context);
        }
        catch (Exception ex)
        {
            return CreateLlmErrorResult($"LLM call failed: {ex.Message}", context);
        }
    }

    /// <summary>
    /// 扫描 Agent 工具端口连接的下游 tool 节点，生成工具定义列表。
    /// </summary>
    internal IReadOnlyList<ToolDefinition> CollectTools(NodeExecutionContext context)
    {
        var workflow = context.Workflow;
        var currentNodeId = context.Node.Id;

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
            if (context.NodeRegistry?.TryGet(toolNode.TypeName, out var resolvedType) == true)
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
                descriptor = context.NodeRegistry?.GetDescriptor(toolNode.TypeName);
            }
            catch (InvalidOperationException)
            {
                // Descriptor not found, skip
            }

            var parametersSchema = SchemaDerivation.DeriveSchema(descriptor?.Parameters);

            tools.Add(new ToolDefinition
            {
                Name = toolNode.Name,
                Description = ResolveToolDescription(nodeType, descriptor),
                TargetNodeDefinitionId = toolNode.Id,
                ParametersSchema = parametersSchema
            });
        }

        return tools;
    }

    private static string ResolveToolDescription(INodeType nodeType, NodeTypeDescriptor? descriptor)
    {
        var description = nodeType.DisplayName;
        if (descriptor?.Parameters is { Count: > 0 })
        {
            var aiParamParam = descriptor.Parameters.FirstOrDefault(p =>
                SchemaDerivation.HasAiParamPlaceholder(p.Description));
            if (aiParamParam?.Description is not null)
            {
                description = SchemaDerivation.ResolveAiParamDescription(aiParamParam.Description)
                    ?? description;
            }
        }

        return description;
    }

    private List<LlmMessage> BuildMessages(NodeExecutionContext context)
    {
        var messages = new List<LlmMessage>();

        if (!string.IsNullOrWhiteSpace(PromptTemplate))
        {
            messages.Add(new LlmMessage { Role = "system", Content = PromptTemplate });
        }

        var inputJson = SerializeInput(context);
        if (inputJson is not null)
        {
            messages.Add(new LlmMessage { Role = "user", Content = inputJson });
        }

        return messages;
    }

    private static string? SerializeInput(NodeExecutionContext context)
    {
        var batch = context.GetInputBatch();
        if (batch.Items.Count == 0)
        {
            return null;
        }

        var firstItem = batch.Items[0];
        if (firstItem.Data is null)
        {
            return null;
        }

        return firstItem.Data.ToJsonString();
    }

    private static NodeExecutionResult CreateSuccessResult(InlineResolverResult result, NodeExecutionContext context)
    {
        var dto = new AgentExecutionResultDto
        {
            AgentInfo = new AgentExecutionInfoDto
            {
                Model = context.LlmClient?.GetType().Name ?? "unknown",
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

        return new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch
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
            }
        };
    }

    private static NodeExecutionResult CreateTimeoutResult(string message, NodeExecutionContext context)
    {
        return CreateAgentFailedResult(FlowConstants.ErrorCodes.Cancelled, "AgentTimeout", message, context);
    }

    private static NodeExecutionResult CreateLlmErrorResult(string message, NodeExecutionContext context)
    {
        return CreateAgentFailedResult("Failed", "LlmError", message, context);
    }

    private static NodeExecutionResult CreateAgentFailedResult(string status, string code, string message, NodeExecutionContext context)
    {
        var dto = new AgentExecutionResultDto
        {
            AgentInfo = new AgentExecutionInfoDto
            {
                Model = context.LlmClient?.GetType().Name ?? "unknown",
                IterationCount = 0,
                Status = status,
                StartedAt = null,
                CompletedAt = DateTime.UtcNow,
                ErrorMessage = message,
                TokenUsage = null,
            },
            Iterations = [],
            SubRecords = [],
            SystemPrompt = null,
        };

        return new NodeExecutionResult
        {
            Success = false,
            Error = new NodeError
            {
                Code = code,
                Message = message,
                NodeDefinitionId = context.Node.Id
            },
            Output = new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = JsonSerializer.SerializeToNode(dto, JsonDefaults.Options),
                        Success = false,
                        SourceIndex = 0,
                    }
                ]
            }
        };
    }
}
