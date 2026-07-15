using System.ClientModel;
using System.Threading.Channels;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using OpenAI;
using OpenAI.Chat;

namespace FlowEngine.Infrastructure.Ai;

/// <summary>
/// OpenAI LLM 客户端适配器，封装 OpenAI Chat Completions API 调用。
/// 已从 <c>FlowEngine.Plugins.Standard</c> 下沉至基础设施层，作为系统级 LLM 客户端实现，
/// 供后端语义解析服务与插件层（<c>LlmNode</c>）共用。
/// </summary>
public sealed class OpenAiLlmClient : ILlmClient
{
    private const int DefaultTimeoutSeconds = 60;

    private readonly OpenAIClient _client;
    private readonly string _model;
    private readonly float _temperature;
    private readonly int? _maxTokens;

    /// <inheritdoc />
    public string ModelName => _model;

    /// <summary>
    /// 初始化 OpenAI LLM 客户端。
    /// </summary>
    /// <param name="apiKey">OpenAI API Key。</param>
    /// <param name="model">模型名称，如 gpt-4。</param>
    /// <param name="temperature">温度参数，0-2。</param>
    /// <param name="maxTokens">最大输出 token 数。</param>
    /// <param name="baseEndpoint">API 基础端点（可选，用于自定义或兼容端点）。</param>
    public OpenAiLlmClient(
        string apiKey,
        string model = "gpt-4",
        float temperature = 0.7f,
        int? maxTokens = null,
        Uri? baseEndpoint = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(model);

        _model = model;
        _temperature = Math.Clamp(temperature, 0f, 2f);
        _maxTokens = maxTokens;

        if (baseEndpoint is not null)
        {
            var options = new OpenAIClientOptions
            {
                Endpoint = baseEndpoint
            };
            _client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        }
        else
        {
            _client = new OpenAIClient(apiKey);
        }
    }

    /// <summary>
    /// 测试专用构造函数，允许注入预配置的 <see cref="OpenAIClient"/>。
    /// </summary>
    internal OpenAiLlmClient(
        OpenAIClient client,
        string model = "gpt-4",
        float temperature = 0.7f,
        int? maxTokens = null)
    {
        _client = client;
        _model = model;
        _temperature = Math.Clamp(temperature, 0f, 2f);
        _maxTokens = maxTokens;
    }

    /// <inheritdoc />
    public async Task<LlmResponse> ChatAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken = default)
    {
        var chatMessages = ConvertMessages(messages);

        var chatOptions = new ChatCompletionOptions
        {
            Temperature = _temperature,
        };

        if (_maxTokens.HasValue)
        {
            chatOptions.MaxOutputTokenCount = _maxTokens.Value;
        }

        foreach (var tool in ConvertTools(tools))
        {
            chatOptions.Tools.Add(tool);
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var chatClient = _client.GetChatClient(_model);
            var response = await chatClient.CompleteChatAsync(chatMessages, chatOptions, linkedCts.Token)
                .ConfigureAwait(false);

            return ConvertResponse(response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"OpenAI API call timed out after {DefaultTimeoutSeconds} seconds.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"OpenAI API call failed: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<LlmStreamChunk> ChatStreamAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<LlmStreamChunk>();
        _ = ProduceStreamAsync(channel.Writer, messages, tools, cancellationToken);
        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    private async Task ProduceStreamAsync(
        ChannelWriter<LlmStreamChunk> writer,
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        try
        {
            var chatMessages = ConvertMessages(messages);

            var chatOptions = new ChatCompletionOptions
            {
                Temperature = _temperature,
            };

            if (_maxTokens.HasValue)
            {
                chatOptions.MaxOutputTokenCount = _maxTokens.Value;
            }

            foreach (var tool in ConvertTools(tools))
            {
                chatOptions.Tools.Add(tool);
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var chatClient = _client.GetChatClient(_model);
            var updates = chatClient.CompleteChatStreamingAsync(chatMessages, chatOptions, linkedCts.Token);

            var toolCallAccumulator = new Dictionary<int, (string Id, string Name, string Arguments)>(4);
            IReadOnlyList<LlmToolCall>? finalToolCalls = null;
            string? finishReason = null;

            await foreach (var update in updates.ConfigureAwait(false))
            {
                if (update.ContentUpdate is { Count: > 0 })
                {
                    foreach (var part in update.ContentUpdate)
                    {
                        var text = part.Text;
                        if (string.IsNullOrEmpty(text))
                        {
                            continue;
                        }

                        await writer.WriteAsync(new LlmStreamChunk { Delta = text }, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                if (update.ToolCallUpdates is { Count: > 0 })
                {
                    foreach (var toolCallUpdate in update.ToolCallUpdates)
                    {
                        var index = toolCallUpdate.Index;
                        if (!toolCallAccumulator.TryGetValue(index, out var existing))
                        {
                            existing = (string.Empty, string.Empty, string.Empty);
                        }

                        if (!string.IsNullOrEmpty(toolCallUpdate.ToolCallId))
                        {
                            existing.Id = toolCallUpdate.ToolCallId;
                        }

                        if (!string.IsNullOrEmpty(toolCallUpdate.FunctionName))
                        {
                            existing.Name = toolCallUpdate.FunctionName;
                        }

                        if (toolCallUpdate.FunctionArgumentsUpdate is { } argsUpdate)
                        {
                            existing.Arguments += argsUpdate.ToString();
                        }

                        toolCallAccumulator[index] = existing;
                    }
                }

                if (update.FinishReason is not null)
                {
                    finishReason = update.FinishReason.ToString();
                }

                if (update.FinishReason is not null && toolCallAccumulator.Count > 0)
                {
                    finalToolCalls = toolCallAccumulator
                        .OrderBy(kv => kv.Key)
                        .Select(kv => new LlmToolCall
                        {
                            Id = kv.Value.Id,
                            Name = kv.Value.Name,
                            Arguments = string.IsNullOrEmpty(kv.Value.Arguments) ? "{}" : kv.Value.Arguments
                        })
                        .ToList();
                }
            }

            await writer.WriteAsync(new LlmStreamChunk
            {
                Delta = null,
                ToolCalls = finalToolCalls,
                IsFinal = true,
                FinishReason = finishReason,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            writer.TryComplete(new TimeoutException($"OpenAI API call timed out after {DefaultTimeoutSeconds} seconds."));
            return;
        }
        catch (Exception ex)
        {
            writer.TryComplete(new InvalidOperationException($"OpenAI API call failed: {ex.Message}", ex));
            return;
        }

        writer.TryComplete();
    }

    private static IReadOnlyList<ChatMessage> ConvertMessages(IReadOnlyList<LlmMessage> messages)
    {
        var result = new List<ChatMessage>(messages.Count);

        foreach (var msg in messages)
        {
            switch (msg.Role)
            {
                case "system":
                    result.Add(ChatMessage.CreateSystemMessage(msg.Content ?? string.Empty));
                    break;
                case "user":
                    result.Add(ChatMessage.CreateUserMessage(msg.Content ?? string.Empty));
                    break;
                case "assistant":
                    if (msg.ToolCalls is { Count: > 0 })
                    {
                        var toolCalls = msg.ToolCalls.Select(tc =>
                            ChatToolCall.CreateFunctionToolCall(tc.Id, tc.Name, BinaryData.FromString(tc.Arguments))
                        ).ToList();

                        result.Add(new AssistantChatMessage(toolCalls));
                    }
                    else
                    {
                        result.Add(ChatMessage.CreateAssistantMessage(msg.Content ?? string.Empty));
                    }
                    break;
                case "tool":
                    result.Add(ChatMessage.CreateToolMessage(msg.ToolCallId ?? string.Empty, msg.Content ?? string.Empty));
                    break;
                default:
                    result.Add(ChatMessage.CreateUserMessage(msg.Content ?? string.Empty));
                    break;
            }
        }

        return result;
    }

    private static IReadOnlyList<ChatTool> ConvertTools(IReadOnlyList<ToolDefinition> tools)
    {
        if (tools.Count == 0)
        {
            return [];
        }

        var result = new List<ChatTool>(tools.Count);

        foreach (var tool in tools)
        {
            var schemaJson = tool.ParametersSchema is System.Text.Json.Nodes.JsonObject schema
                ? schema.ToJsonString()
                : "{}";

            result.Add(ChatTool.CreateFunctionTool(
                tool.Name,
                tool.Description,
                BinaryData.FromString(schemaJson)));
        }

        return result;
    }

    private static LlmResponse ConvertResponse(ChatCompletion response)
    {
        return BuildLlmResponse(
            response.Content?.ToString(),
            response.FinishReason.ToString(),
            response.ToolCalls);
    }

    /// <summary>
    /// 由原始内容、结束原因与工具调用构建 <see cref="LlmResponse"/>。
    /// 与具体 SDK 类型解耦，便于单元测试；同时修复旧实现仅在
    /// <c>Stop</c> 时填充 <see cref="LlmResponse.Content"/> 的问题——
    /// 命中 <c>Length</c>（截断）或 <c>ContentFilter</c>（内容过滤）时
    /// 仍会保留已产出的文本内容并标记结束原因，避免上层拿到静默空结果。
    /// </summary>
    internal static LlmResponse BuildLlmResponse(
        string? rawContent,
        string? finishReason,
        IReadOnlyList<ChatToolCall>? toolCalls)
    {
        var result = new LlmResponse
        {
            FinishReason = finishReason,
        };

        // 只要存在非空文本内容即保留，不局限于 Stop（截断/过滤时仍可能有部分内容）。
        if (!string.IsNullOrEmpty(rawContent))
        {
            result.Content = rawContent;
        }

        if (toolCalls is { Count: > 0 })
        {
            result.ToolCalls = toolCalls.Select(tc => new LlmToolCall
            {
                Id = tc.Id,
                Name = tc.FunctionName,
                Arguments = tc.FunctionArguments?.ToString() ?? "{}"
            }).ToList();
        }

        return result;
    }
}
