using System.Text;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Dtos;
using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Agent;

/// <summary>
/// 内联工具调用解析器，处理 Agent 节点的工具调用循环。
/// </summary>
/// <remarks>
/// 创建 InlineResolver 实例。
/// </remarks>
public sealed class InlineResolver(
    ILlmClient llmClient,
    IReadOnlyList<ToolDefinition> tools,
    NodeExecutionContext parentContext,
    int maxIterations = 10,
    Guid? parentRecordId = null,
    AgentMemory? memory = null,
    IExecutionLogger? logger = null)
{
    private readonly ToolResolver _toolResolver = new(tools, parentContext);
    private readonly ToolContextFactory _contextFactory = new(parentContext, logger);
    private readonly ToolExecutionRecorder _recorder = new(logger);
    private readonly List<NodeExecutionRecord> _toolExecutionRecords = [];


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
        if (memory is not null && memory.Count > 0)
        {
            var memoryMessages = memory.GetMessages();
            var insertIndex = messages.Count > 0 && messages[0].Role == "system" ? 1 : 0;
            messages.InsertRange(insertIndex, memoryMessages);
        }

        var iterations = new List<AgentIterationDto>();
        var finalContent = string.Empty;
        var stopReason = InlineResolverStopReason.MaxIterationsReached;

        for (var i = 0; i < maxIterations; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                stopReason = InlineResolverStopReason.Cancelled;
                finalContent = string.Empty;
                break;
            }

            var iterationStartedAt = DateTime.UtcNow;

            LlmResponse response;
            try
            {
                response = await StreamChatOnceAsync(messages, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                stopReason = InlineResolverStopReason.Cancelled;
                finalContent = string.Empty;
                break;
            }

            var assistantContent = response.Content ?? string.Empty;
            var assistantMessage = new LlmMessage
            {
                Role = "assistant",
                Content = response.Content,
                ToolCalls = response.ToolCalls
            };
            messages.Add(assistantMessage);
            memory?.AddMessage(assistantMessage);

            var toolResults = new List<ToolResult>();

            if (response.HasToolCalls)
            {
                foreach (var toolCall in response.ToolCalls!)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        stopReason = InlineResolverStopReason.Cancelled;
                        finalContent = string.Empty;
                        break;
                    }

                    var toolResult = await ExecuteToolAsync(toolCall, cancellationToken)
                        .ConfigureAwait(false);
                    toolResults.Add(toolResult);

                    var toolMessage = new LlmMessage
                    {
                        Role = "tool",
                        ToolCallId = toolCall.Id,
                        Content = toolResult.Output?.ToString()
                    };
                    messages.Add(toolMessage);
                    memory?.AddMessage(toolMessage);
                }
            }

            var iteration = new AgentIterationDto
            {
                Index = iterations.Count,
                LlmChunks = new List<LlmChunkDto>
                {
                    new() { Content = assistantContent, Role = "assistant", Timestamp = DateTime.UtcNow.ToString("O") }
                },
                ToolCalls = toolResults.Select(tr => new ToolCallRecordDto
                {
                    Id = tr.ToolCallId ?? Guid.NewGuid().ToString(),
                    ToolName = tr.ToolName,
                    Input = tr.Input,
                    Output = tr.Output,
                    Status = tr.Success ? "Completed" : "Failed",
                    Duration = null,
                    Error = tr.Error,
                }).ToList(),
                StartedAt = iterationStartedAt.ToString("O"),
                CompletedAt = DateTime.UtcNow.ToString("O"),
            };
            iterations.Add(iteration);

            if (stopReason == InlineResolverStopReason.Cancelled)
            {
                break;
            }

            if (!response.HasToolCalls)
            {
                finalContent = assistantContent;
                stopReason = InlineResolverStopReason.Completed;
                break;
            }
        }

        var result = new InlineResolverResult
        {
            Content = finalContent,
            StoppedReason = stopReason,
            Iterations = iterations,
        };
        result.ToolExecutionRecords.AddRange(_toolExecutionRecords);
        return result;
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
        var callback = parentContext.OnLlmStreamChunk;
        var callbackErrorLogged = false;

        await foreach (var chunk in llmClient.ChatStreamAsync(messages, tools, cancellationToken)
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
                try
                {
                    await callback(chunk, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (!callbackErrorLogged)
                    {
                        logger?.LogWarning("LLM 流式回调执行失败（后续错误将被抑制）：{Message}", ex.Message);
                        callbackErrorLogged = true;
                    }
                }
            }
        }

        return new LlmResponse
        {
            Content = contentBuilder.ToString(),
            ToolCalls = toolCalls,
        };
    }

    private async Task<ToolResult> ExecuteToolAsync(
        LlmToolCall toolCall,
        CancellationToken cancellationToken)
    {
        var resolution = _toolResolver.Resolve(toolCall);
        if (resolution.HasError)
        {
            return ToolResultFactory.Error(toolCall, resolution.Error!);
        }

        JsonNode? args;
        try
        {
            args = JsonNode.Parse(toolCall.Arguments);
        }
        catch (Exception ex)
        {
            logger?.LogWarning("解析工具调用参数失败：{Message}", ex.Message);
            args = null;
        }

        var inputBatch = new DataBatch
        {
            Items = [new DataItem { Data = args, Success = true, SourceIndex = 0 }]
        };

        var startedAt = DateTime.UtcNow;
        var (toolContext, toolNodeInstance) = await _contextFactory.CreateAsync(
            resolution, inputBatch, startedAt, cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await toolNodeInstance.ExecuteAsync(toolContext, cancellationToken)
                .ConfigureAwait(false);
            var record = _recorder.Record(resolution.Node!, toolContext, result, startedAt, parentRecordId);
            _toolExecutionRecords.Add(record);
            return ToolResultFactory.FromExecutionResult(toolCall, args, result);
        }
        catch (Exception ex)
        {
            var errorResult = new NodeExecutionResult
            {
                Success = false,
                Error = new NodeError
                {
                    Code = "UnexpectedError",
                    Message = ex.Message ?? string.Empty,
                    NodeDefinitionId = resolution.Node?.Id.ToString() ?? string.Empty
                }
            };
            var record = _recorder.Record(resolution.Node!, toolContext, errorResult, startedAt, parentRecordId);
            _toolExecutionRecords.Add(record);
            var message = $"Tool execution error: {ex.Message}";
            return ToolResultFactory.Error(toolCall, args, message);
        }
    }
}
