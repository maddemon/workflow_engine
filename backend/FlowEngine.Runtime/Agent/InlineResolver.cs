using System.Text;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Dtos;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Tools;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Agent;

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
    ILogger? logger = null)
{

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

        for (var i = 0; i < _maxIterations; i++)
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
                    _memory?.AddMessage(toolMessage);
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

        return new InlineResolverResult
        {
            Content = finalContent,
            StoppedReason = stopReason,
            Iterations = iterations,
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
        var callback = parentContext.OnLlmStreamChunk;

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
                await callback(chunk, cancellationToken).ConfigureAwait(false);
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
        var tool = tools.FirstOrDefault(t => t.Name == toolCall.Name);
        if (tool is null)
        {
            var message = $"Tool '{toolCall.Name}' not found.";
            var output = ResultSanitizer.Sanitize(toolCall.Name, message);
            return new ToolResult(toolCall.Id, toolCall.Name, null, output, false, message);
        }

        var toolNode = parentContext.Workflow.Nodes
            .FirstOrDefault(n => n.Id == tool.TargetNodeDefinitionId);
        if (toolNode is null)
        {
            var message = $"Tool node '{tool.TargetNodeDefinitionId}' not found.";
            var output = ResultSanitizer.Sanitize(toolCall.Name, message);
            return new ToolResult(toolCall.Id, toolCall.Name, null, output, false, message);
        }

        if (parentContext.NodeRegistry?.TryGet(toolNode.TypeName, out var nodeType) != true
            || nodeType is null)
        {
            var message = $"Node type '{toolNode.TypeName}' not found.";
            var output = ResultSanitizer.Sanitize(toolCall.Name, message);
            return new ToolResult(toolCall.Id, toolCall.Name, null, output, false, message);
        }

        JsonNode? args;
        try
        {
            args = JsonNode.Parse(toolCall.Arguments);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "解析工具调用参数失败。");
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
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "创建工具节点实例失败，类型：{TypeName}。", nodeType.GetType().Name);
            toolNodeInstance = null;
        }

        toolNodeInstance ??= nodeType;

        NodeExecutionContext toolContext;
        if (parentContext.ContextFactory is not null && toolNodeInstance is not null)
        {
            var execution = new ExecutionRecord
            {
                Id = parentContext.ExecutionId,
                WorkflowDefinitionId = parentContext.Workflow.Id,
                ProjectId = parentContext.Workflow.ProjectId, // 冗余存储（GAP-11）
                StartedAt = startedAt,
                Status = ExecutionStatus.Running,
            };

            toolContext = await parentContext.ContextFactory.CreateAsync(
                parentContext.Workflow,
                execution,
                toolNode,
                toolNodeInstance,
                new Dictionary<string, DataBatch> { [FlowConstants.PortNames.Input] = inputBatch },
                new Dictionary<string, DataBatch>(),
                new Dictionary<string, DataBatch>(),
                0,
                cancellationToken).ConfigureAwait(false);
            // ContextFactory 不感知嵌套深度，需在此处显式递增（GAP-03）。
            toolContext.NestingDepth = parentContext.NestingDepth + 1;
        }
        else
        {
            toolContext = new NodeExecutionContext
            {
                Workflow = parentContext.Workflow,
                ExecutionId = parentContext.ExecutionId,
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
                Credentials = parentContext.Credentials,
                Logger = parentContext.Logger,
                CancellationToken = cancellationToken,
                NestingDepth = parentContext.NestingDepth + 1
            };
        }

        if (toolNodeInstance is null)
        {
            var message = $"Failed to create instance for node type '{toolNode.TypeName}'.";
            var output = ResultSanitizer.Sanitize(toolCall.Name, message);
            return new ToolResult(toolCall.Id, toolCall.Name, args, output, false, message);
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
                ParentRecordId = parentRecordId
            };

            if (!result.Success)
            {
                var message = $"Tool execution failed: {result.Error?.Message ?? "Unknown error"}";
                var errorOutput = ResultSanitizer.Sanitize(toolCall.Name, message);
                return new ToolResult(toolCall.Id, toolCall.Name, args, errorOutput, false, message);
            }

            object? output;
            if (result.Output.Items.Count > 0)
            {
                var data = result.Output.Items[0].Data;
                if (data is not null)
                {
                    output = data.ToJsonString();
                }
                else
                {
                    output = "Tool executed successfully.";
                }
            }
            else
            {
                output = "Tool executed successfully.";
            }

            return new ToolResult(toolCall.Id, toolCall.Name, args, output, true, null);
        }
        catch (Exception ex)
        {
            var message = $"Tool execution error: {ex.Message}";
            var output = ResultSanitizer.Sanitize(toolCall.Name, message);
            return new ToolResult(toolCall.Id, toolCall.Name, args, output, false, message);
        }
    }

    private sealed record ToolResult(
        string ToolCallId,
        string ToolName,
        object? Input,
        object? Output,
        bool Success,
        string? Error);
}
