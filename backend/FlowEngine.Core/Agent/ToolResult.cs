namespace FlowEngine.Core.Agent;

/// <summary>
/// 工具调用结果，供 InlineResolver 内部及协作类共享。
/// </summary>
internal sealed record ToolResult(
    string ToolCallId,
    string ToolName,
    object? Input,
    object? Output,
    bool Success,
    string? Error);
