namespace FlowEngine.Core.Entities;

/// <summary>
/// 构造安全的 <see cref="NodeError"/>，避免向客户端泄露堆栈跟踪（<see cref="NodeError.StackTrace"/>）
/// 或原始异常消息（<see cref="Exception.Message"/>，可能包含表名、路径等敏感信息）。
/// 异常的完整细节（含堆栈）必须由调用方通过 <c>ILogger</c> 记录到服务端日志，不得外泄到客户端。
/// </summary>
public static class NodeErrorFactory
{
    /// <summary>
    /// 安全错误信息（不依赖任何异常内部文本）。
    /// </summary>
    public const string SafeMessage = "节点执行过程中发生错误，详细信息已记录至服务端日志。";

    /// <summary>
    /// 由异常构造安全错误（不含 <see cref="NodeError.StackTrace"/> 与原始 <see cref="Exception.Message"/>）。
    /// </summary>
    /// <param name="ex">原始异常（仅用于服务端日志，不读取其文本）。</param>
    /// <param name="code">安全错误码。</param>
    /// <param name="nodeDefinitionId">节点定义 ID。</param>
    public static NodeError Sanitize(Exception ex, string code, string nodeDefinitionId)
    {
        // 故意不读取 ex.Message / ex.StackTrace，避免敏感信息（表名、路径、连接串）外泄到客户端。
        // 完整异常已由调用方经 ILogger 记录。
        return new NodeError
        {
            Code = code,
            Message = SafeMessage,
            NodeDefinitionId = nodeDefinitionId
        };
    }

    /// <summary>
    /// 由异常构造安全错误，使用调用方提供的非敏感通用描述。
    /// </summary>
    /// <param name="ex">原始异常（仅用于服务端日志，不读取其文本）。</param>
    /// <param name="code">安全错误码。</param>
    /// <param name="nodeDefinitionId">节点定义 ID。</param>
    /// <param name="safeMessage">非敏感的通用错误描述（不得包含原始异常文本或敏感细节）。</param>
    public static NodeError Sanitize(Exception ex, string code, string nodeDefinitionId, string safeMessage)
    {
        return new NodeError
        {
            Code = code,
            Message = safeMessage,
            NodeDefinitionId = nodeDefinitionId
        };
    }

    /// <summary>
    /// 将可能为 null 或含敏感原始文本的 <see cref="NodeError"/> 转换为客户端安全的副本：
    /// 保留 <see cref="NodeError.Code"/> 与 <see cref="NodeError.NodeDefinitionId"/>（供前端定位节点），
    /// 但丢弃 <see cref="NodeError.Message"/>、<see cref="NodeError.StackTrace"/> 与 <see cref="NodeError.Details"/>
    /// 中的原始异常文本（可能含表名、路径、连接串等敏感信息），统一替换为通用安全描述。
    /// <para>用于节点/工作流错误事件推送至 WebSocket / SSE 等客户端通道前的边界脱敏；
    /// 服务端审计与错误触发消费仍使用原始 <see cref="NodeErrorEvent"/>，不受此影响。</para>
    /// </summary>
    /// <param name="error">原始节点错误（可能为 null）。</param>
    public static NodeError ToClientSafe(NodeError? error)
    {
        if (error is null)
        {
            return new NodeError { Code = "NodeExecutionFailed", Message = SafeMessage };
        }

        // 原始 Message / StackTrace / Details 可能包含异常内部文本，一律丢弃；
        // 完整异常已由节点执行处经 ILogger 记录到服务端日志。
        return new NodeError
        {
            Code = error.Code,
            Message = SafeMessage,
            NodeDefinitionId = error.NodeDefinitionId
        };
    }
}
