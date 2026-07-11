using System.Runtime.CompilerServices;
using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// LLM 客户端契约，负责与大语言模型通信。
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// 向 LLM 发送对话请求并获取响应。
    /// </summary>
    /// <param name="messages">对话消息列表。</param>
    /// <param name="tools">可用工具定义列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>LLM 响应。</returns>
    Task<LlmResponse> ChatAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 向 LLM 发送对话请求并以流式方式获取增量 token。
    /// 默认实现基于 <see cref="ChatAsync"/> 包装为单条最终 chunk，
    /// 具体实现可重写为真正的流式 API。
    /// </summary>
    /// <param name="messages">对话消息列表。</param>
    /// <param name="tools">可用工具定义列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>流式增量 chunk 枚举。</returns>
    IAsyncEnumerable<LlmStreamChunk> ChatStreamAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken = default)
        => DefaultChatStreamAsync(this, messages, tools, cancellationToken);

    private static async IAsyncEnumerable<LlmStreamChunk> DefaultChatStreamAsync(
        ILlmClient client,
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await client.ChatAsync(messages, tools, cancellationToken)
            .ConfigureAwait(false);

        yield return new LlmStreamChunk
        {
            Delta = response.Content,
            ToolCalls = response.ToolCalls,
            IsFinal = true,
            FinishReason = response.FinishReason,
        };
    }
}
