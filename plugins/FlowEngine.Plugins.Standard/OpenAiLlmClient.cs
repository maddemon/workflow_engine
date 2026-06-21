using System.ClientModel;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using OpenAI;
using OpenAI.Chat;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// OpenAI LLM 客户端适配器，封装 OpenAI Chat Completions API 调用。
/// </summary>
public sealed class OpenAiLlmClient : ILlmClient
{
    private const int DefaultTimeoutSeconds = 60;

    private readonly OpenAIClient _client;
    private readonly string _model;
    private readonly float _temperature;
    private readonly int? _maxTokens;

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
        var result = new LlmResponse();

        if (response.FinishReason == ChatFinishReason.Stop)
        {
            result.Content = response.Content?.ToString() ?? string.Empty;
        }

        if (response.ToolCalls.Count > 0)
        {
            result.ToolCalls = response.ToolCalls.Select(tc => new LlmToolCall
            {
                Id = tc.Id,
                Name = tc.FunctionName,
                Arguments = tc.FunctionArguments?.ToString() ?? "{}"
            }).ToList();
        }

        return result;
    }
}
