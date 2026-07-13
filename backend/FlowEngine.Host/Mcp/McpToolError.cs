namespace FlowEngine.Host.Mcp;

/// <summary>
/// MCP 工具失败时的统一返回结构。
/// </summary>
/// <param name="ErrorCode">错误码。</param>
/// <param name="Message">人类可读错误信息。</param>
/// <param name="CanAutoFix">AI 是否可自动修复。</param>
/// <param name="SuggestedFix">给 AI 的建议修复方案。</param>
public sealed record McpToolError(
    string ErrorCode,
    string Message,
    bool CanAutoFix = false,
    string? SuggestedFix = null)
{
    /// <summary>
    /// 统一标识失败状态，便于 AI 客户端识别。
    /// </summary>
    public bool Success => false;
}
