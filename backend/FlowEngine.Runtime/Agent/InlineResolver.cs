using System.Text;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Tools;

namespace FlowEngine.Runtime.Agent;

/// <summary>
/// 内联工具调用解析器，处理 Agent 节点的工具调用循环。
/// </summary>
public sealed class InlineResolver
{
    private readonly ILlmClient _llmClient;
    private readonly IReadOnlyList<ToolDefinition> _tools;
    private readonly NodeExecutionContext _parentContext;
    private readonly int _maxIterations;
    private readonly Guid? _parentRecordId;
    private readonly AgentMemory? _memory;

    /// <summary>
    /// 创建 InlineResolver 实例。
    /// </summary>
    public InlineResolver(
        ILlmClient llmClient,
        IReadOnlyList<ToolDefinition> tools,
        NodeExecutionContext parentContext,
        int maxIterations = 10,
        Guid? parentRecordId = null,
        AgentMemory? memory = null)
    {
        _llmClient = llmClient;
        _tools = tools;
        _parentContext = parentContext;
        _maxIterations = maxIterations;
        _parentRecordId = parentRecordId;
        _memory = memory;
    }

    /// <summary>
    /// 执行工具调用循环，直到 LLM 返回无工具调用或达到最大迭代次数。
    /// </summary>
    /// <param name="messages">对话消息列表（会被修改）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>最终 LLM 响应内容。</returns>
    public async Task<InlineResolverResult> RunAsync(
        List<LlmMessage> messages,
        CancellationToken cancellationToken = default)
    {
        // 若启用记忆，先把历史记忆合并到消息列表前部（system prompt 之后），
        // 使 Agent 能引用前序轮次的上下文（GAP-02）。
        if (_memory is not null && _memory.Count > 0)
        {
            var memoryMessages = _memory.GetMessages();
            var insertIndex = messages.Count > 0 && messages[0].Role == "system" ? 1 : 0;
            messages.InsertRange(insertIndex, memoryMessages);
        }

        for (var i = 0; i < _maxIterations; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new InlineResolverResult
                {
                    Content = string.Empty,
                    Iterations = i,
                    StoppedReason = InlineResolverStopReason.Cancelled
                };
            }

            LlmResponse response;
            try
            {
                response = await StreamChatOnceAsync(messages, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new InlineResolverResult
                {
                    Content = string.Empty,
                    Iterations = i,
                    StoppedReason = InlineResolverStopReason.Cancelled
                };
            }

            var assistantMessage = new LlmMessage
            {
                Role = "assistant",
                Content = response.Content,
                ToolCalls = response.ToolCalls
            };
            messages.Add(assistantMessage);
            _memory?.AddMessage(assistantMessage);

            if (!response.HasToolCalls)
            {
                return new InlineResolverResult
                {
                    Content = response.Content ?? string.Empty,
                    Iterations = i + 1,
                    StoppedReason = InlineResolverStopReason.Completed
                };
            }

            foreach (var toolCall in response.ToolCalls!)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return new InlineResolverResult
                    {
                        Content = string.Empty,
                        Iterations = i + 1,
                        StoppedReason = InlineResolverStopReason.Cancelled
                    };
                }

                var toolResult = await ExecuteToolAsync(toolCall, cancellationToken)
                    .ConfigureAwait(false);

                var toolMessage = new LlmMessage
                {
                    Role = "tool",
                    ToolCallId = toolCall.Id,
                    Content = toolResult
                };
                messages.Add(toolMessage);
                _memory?.AddMessage(toolMessage);
            }
        }

        return new InlineResolverResult
        {
            Content = string.Empty,
            Iterations = _maxIterations,
            StoppedReason = InlineResolverStopReason.MaxIterationsReached
        };
    }

    /// <summary>
    /// 调用 LLM 流式接口获取单次响应，逐 chunk 触发上下文回调，
    /// 并将累积内容与工具调用封装为 <see cref="LlmResponse"/>。
    /// </summary>
    private async Task<LlmResponse> StreamChatOnceAsync(
        List<LlmMessage> messages,
        CancellationToken cancellationToken)
    {
        var contentBuilder = new StringBuilder();
        IReadOnlyList<LlmToolCall>? toolCalls = null;
        var callback = _parentContext.OnLlmStreamChunk;

        await foreach (var chunk in _llmClient.ChatStreamAsync(messages, _tools, cancellationToken)
            .ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(chunk.Delta))
            {
                contentBuilder.Append(chunk.Delta);
            }

            if (chunk.IsFinal)
            {
                toolCalls = chunk.ToolCalls;
            }

            if (callback is not null)
            {
                await callback(chunk, cancellationToken).ConfigureAwait(false);
            }
        }

        return new LlmResponse
        {
            Content = contentBuilder.ToString(),
            ToolCalls = toolCalls,
        };
    }

    private async Task<string> ExecuteToolAsync(
        LlmToolCall toolCall,
        CancellationToken cancellationToken)
    {
        var tool = _tools.FirstOrDefault(t => t.Name == toolCall.Name);
        if (tool is null)
        {
            return ResultSanitizer.Sanitize(toolCall.Name, $"Tool '{toolCall.Name}' not found.");
        }

        var toolNode = _parentContext.Workflow.Nodes
            .FirstOrDefault(n => n.Id == tool.TargetNodeDefinitionId);
        if (toolNode is null)
        {
            return ResultSanitizer.Sanitize(toolCall.Name, $"Tool node '{tool.TargetNodeDefinitionId}' not found.");
        }

        if (_parentContext.NodeRegistry?.TryGet(toolNode.TypeName, out var nodeType) != true
            || nodeType is null)
        {
            return ResultSanitizer.Sanitize(toolCall.Name, $"Node type '{toolNode.TypeName}' not found.");
        }

        JsonNode? args;
        try
        {
            args = JsonNode.Parse(toolCall.Arguments);
        }
        catch
        {
            args = null;
        }

        var inputBatch = new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = args,
                    Success = true,
                    SourceIndex = 0
                }
            ]
        };

        var startedAt = DateTime.UtcNow;
        INodeType? toolNodeInstance;

        try
        {
            toolNodeInstance = (INodeType?)Activator.CreateInstance(nodeType.GetType());
        }
        catch
        {
            toolNodeInstance = null;
        }

        toolNodeInstance ??= nodeType;

        NodeExecutionContext toolContext;
        if (_parentContext.ContextFactory is not null && toolNodeInstance is not null)
        {
            var execution = new ExecutionRecord
            {
                Id = _parentContext.ExecutionId,
                WorkflowDefinitionId = _parentContext.Workflow.Id,
                ProjectId = _parentContext.Workflow.ProjectId, // 冗余存储（GAP-11）
                StartedAt = startedAt,
                Status = ExecutionStatus.Running,
            };

            toolContext = await _parentContext.ContextFactory.CreateAsync(
                _parentContext.Workflow,
                execution,
                toolNode,
                toolNodeInstance,
                new Dictionary<string, DataBatch> { [FlowConstants.PortNames.Input] = inputBatch },
                new Dictionary<string, DataBatch>(),
                new Dictionary<string, DataBatch>(),
                0,
                cancellationToken).ConfigureAwait(false);
            // ContextFactory 不感知嵌套深度，需在此处显式递增（GAP-03）。
            toolContext.NestingDepth = _parentContext.NestingDepth + 1;
        }
        else
        {
            toolContext = new NodeExecutionContext
            {
                Workflow = _parentContext.Workflow,
                ExecutionId = _parentContext.ExecutionId,
                Node = new NodeDefinition
                {
                    Id = toolNode.Id,
                    TypeName = toolNode.TypeName,
                    Name = toolNode.Name,
                    Parameters = toolNode.Parameters,
                    Ports = toolNode.Ports
                },
                Inputs = new Dictionary<string, DataBatch> { [FlowConstants.PortNames.Input] = inputBatch },
                RawParameters = toolNode.Parameters,
                ResolvedParameters = toolNode.Parameters,
                Credentials = _parentContext.Credentials,
                Logger = _parentContext.Logger,
                CancellationToken = cancellationToken,
                NestingDepth = _parentContext.NestingDepth + 1
            };
        }

        if (toolNodeInstance is null)
        {
            return ResultSanitizer.Sanitize(toolCall.Name, $"Failed to create instance for node type '{toolNode.TypeName}'.");
        }

        try
        {
            var result = await toolNodeInstance.ExecuteAsync(toolContext, cancellationToken)
                .ConfigureAwait(false);

            var record = new NodeExecutionRecord
            {
                Id = Guid.NewGuid(),
                NodeDefinitionId = toolNode.Id,
                RunIndex = 0,
                StartedAt = startedAt,
                CompletedAt = DateTime.UtcNow,
                Inputs = toolContext.Inputs.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                Output = result,
                RawParameters = toolContext.RawParameters.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                ResolvedParameters = toolContext.ResolvedParameters.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                ParentRecordId = _parentRecordId
            };

            if (!result.Success)
            {
                return ResultSanitizer.Sanitize(toolCall.Name, $"Tool execution failed: {result.Error?.Message ?? "Unknown error"}");
            }

            if (result.Output.Items.Count > 0)
            {
                var data = result.Output.Items[0].Data;
                if (data is not null)
                {
                    return ResultSanitizer.Sanitize(toolCall.Name, data.ToJsonString());
                }
            }

            return ResultSanitizer.Sanitize(toolCall.Name, "Tool executed successfully.");
        }
        catch (Exception ex)
        {
            return ResultSanitizer.Sanitize(toolCall.Name, $"Tool execution error: {ex.Message}");
        }
    }
}
