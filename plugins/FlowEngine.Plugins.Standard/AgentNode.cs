using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Agent;
using FlowEngine.Core.Dtos;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Tools;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// AI Agent 节点，通过 LLM 循环调用下游工具节点完成任务。
/// </summary>
[NodeMeta(TypeName = "agent", DisplayName = "Agent", Category = NodeCategory.AI, Icon = "bot", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
[Port(FlowConstants.PortNames.Tools, "Tool", PortDirection.Input, PortType.AgentTool)]
[Port(FlowConstants.PortNames.Llm, "LLM", PortDirection.Input, PortType.LLM)]
public sealed class AgentNode : NodeBase
{
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
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        if (LlmClient is null)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.MissingLlmClient, "LLM client not available. Connect an LLM supply node.");
        }

        // 生产路径经注入的 SubExecutionService 解析工具；仅当 DI 未注入（遗留/直接实例化测试）时回退到直接读取上下文。
        var tools = SubExecutionService is not null
            ? await SubExecutionService.ResolveAgentToolsAsync(ExecutionContext, ct).ConfigureAwait(false)
            : CollectTools(ExecutionContext);

        var messages = BuildMessages(input);

        var maxIterations = MaxIterations > 0 ? MaxIterations : 10;
        using var timeoutCts = TimeoutSeconds.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;

        if (timeoutCts is not null && TimeoutSeconds.HasValue)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds.Value));
        }

        var effectiveToken = timeoutCts?.Token ?? ct;

        AgentMemory? memory = null;
        if (MemoryEnabled)
        {
            memory = new AgentMemory(MemoryWindowSize > 0 ? MemoryWindowSize : 20);
        }

        // ExecutionContext 作为受控上下文传给注入的解析器（符合 NodeBase 设计：原始上下文仅委派给注入的基础设施服务）。
        var resolver = new InlineResolver(
            LlmClient,
            tools,
            ExecutionContext,
            maxIterations,
            memory: memory,
            logger: Logger);

        try
        {
            var result = await resolver.RunAsync(messages, effectiveToken).ConfigureAwait(false);
            return BuildOutput(result, maxIterations);
        }
        catch (OperationCanceledException) when (timeoutCts is not null)
        {
            return NodeHandlerOutput.Failure(
                "AgentTimeout",
                "LLM call timed out.",
                BuildFailedBatch(FlowConstants.ErrorCodes.Cancelled, "LLM call timed out."),
                ExecutionContext.Node?.Id);
        }
        catch (Exception)
        {
            // EX-2：LLM 调用失败同样不可泄露原始异常文本或堆栈，统一脱敏为安全消息。
            return NodeHandlerOutput.Failure(
                "LlmError",
                NodeErrorFactory.SafeMessage,
                BuildFailedBatch("Failed", NodeErrorFactory.SafeMessage),
                ExecutionContext.Node?.Id);
        }
    }

    /// <summary>
    /// 将解析器结果映射为节点业务输出：成功返回 Data 输出，超时/取消/LLM 错误返回携带结果 DTO 的失败输出。
    /// </summary>
    private NodeHandlerOutput BuildOutput(InlineResolverResult result, int maxIterations)
    {
        return result.StoppedReason switch
        {
            InlineResolverStopReason.Completed => NodeHandlerOutput.Data(BuildSuccessBatch(result)),
            InlineResolverStopReason.MaxIterationsReached => NodeHandlerOutput.Failure(
                "AgentTimeout",
                $"Maximum iterations ({maxIterations}) reached.",
                BuildFailedBatch(FlowConstants.ErrorCodes.Cancelled, $"Maximum iterations ({maxIterations}) reached."),
                ExecutionContext.Node?.Id),
            InlineResolverStopReason.Cancelled => NodeHandlerOutput.Failure(
                "AgentTimeout",
                "LLM call timed out or was cancelled.",
                BuildFailedBatch(FlowConstants.ErrorCodes.Cancelled, "LLM call timed out or was cancelled."),
                ExecutionContext.Node?.Id),
            _ => NodeHandlerOutput.Failure(
                "AgentTimeout",
                "Agent execution stopped.",
                BuildFailedBatch(FlowConstants.ErrorCodes.Cancelled, "Agent execution stopped."),
                ExecutionContext.Node?.Id)
        };
    }

    /// <summary>
    /// 扫描 Agent 工具端口连接的下游 tool 节点，生成工具定义列表（仅当 SubExecutionService 未注入时的回退路径）。
    /// </summary>
    private IReadOnlyList<ToolDefinition> CollectTools(NodeExecutionContext context)
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
            if (ToolRegistry?.TryGet(toolNode.TypeName, out var resolvedType) == true)
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
                descriptor = ToolRegistry?.GetDescriptor(toolNode.TypeName);
            }
            catch (InvalidOperationException)
            {
                // Descriptor not found, skip
            }

            var parametersSchema = SchemaDerivation.DeriveSchema(descriptor?.Parameters);

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

        var inputJson = SerializeInput(input.InputBatch, Logger);
        if (inputJson is not null)
        {
            messages.Add(new LlmMessage { Role = "user", Content = inputJson });
        }

        return messages;
    }

    private static string? SerializeInput(DataBatch batch, IExecutionLogger? logger)
    {
        if (batch.Items.Count == 0)
        {
            return null;
        }

        if (batch.Items.Count > 1)
        {
            logger?.LogWarning("Agent 节点收到 {Count} 条输入数据，仅处理第一条，其余将被忽略。", batch.Items.Count);
        }

        var firstItem = batch.Items[0];
        if (firstItem.Data is null)
        {
            return null;
        }

        return firstItem.Data.ToJsonString();
    }

    private DataBatch BuildSuccessBatch(InlineResolverResult result)
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

    private DataBatch BuildFailedBatch(string status, string errorMessage)
    {
        var dto = new AgentExecutionResultDto
        {
            AgentInfo = new AgentExecutionInfoDto
            {
                Model = LlmClient?.ModelName ?? "unknown",
                IterationCount = 0,
                Status = status,
                StartedAt = null,
                CompletedAt = DateTime.UtcNow,
                ErrorMessage = errorMessage,
                TokenUsage = null,
            },
            Iterations = [],
            SubRecords = [],
            SystemPrompt = null,
        };

        return new DataBatch
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
        };
    }
}
