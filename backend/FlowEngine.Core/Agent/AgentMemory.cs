using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Agent;

/// <summary>
/// Agent 对话记忆管理器，维护滑动窗口消息历史。
/// </summary>
public sealed class AgentMemory
{
    private readonly int _windowSize;
    private readonly List<LlmMessage> _messages = [];

    /// <summary>
    /// 创建 AgentMemory 实例。
    /// </summary>
    /// <param name="windowSize">保留最近 N 条消息。</param>
    public AgentMemory(int windowSize = 20)
    {
        _windowSize = Math.Max(1, windowSize);
    }

    /// <summary>
    /// 当前记忆中的消息数量。
    /// </summary>
    public int Count => _messages.Count;

    /// <summary>
    /// 添加一条消息到记忆中，超出窗口大小时自动裁剪旧消息。
    /// </summary>
    /// <param name="message">要添加的消息。</param>
    public void AddMessage(LlmMessage message)
    {
        _messages.Add(message);
        Trim();
    }

    /// <summary>
    /// 批量添加消息到记忆中。
    /// </summary>
    /// <param name="messages">要添加的消息列表。</param>
    public void AddMessages(IEnumerable<LlmMessage> messages)
    {
        _messages.AddRange(messages);
        Trim();
    }

    /// <summary>
    /// 获取格式化的消息列表，供 LLM 调用使用。
    /// </summary>
    /// <returns>消息列表的只读副本。</returns>
    public IReadOnlyList<LlmMessage> GetMessages()
    {
        return _messages.AsReadOnly();
    }

    /// <summary>
    /// 清空所有消息。
    /// </summary>
    public void Clear()
    {
        _messages.Clear();
    }

    /// <summary>
    /// 将消息添加到记忆并返回完整列表（合并现有记忆与新消息）。
    /// </summary>
    /// <param name="newMessages">新消息列表。</param>
    /// <returns>合并后的消息列表。</returns>
    public List<LlmMessage> MergeAndReturnAll(IEnumerable<LlmMessage> newMessages)
    {
        _messages.AddRange(newMessages);
        Trim();
        return [.. _messages];
    }

    private void Trim()
    {
        if (_messages.Count <= _windowSize)
        {
            return;
        }

        var excess = _messages.Count - _windowSize;
        _messages.RemoveRange(0, excess);
    }
}
