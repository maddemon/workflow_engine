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
}
