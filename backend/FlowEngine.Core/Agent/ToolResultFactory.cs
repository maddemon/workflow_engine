using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Tools;

namespace FlowEngine.Core.Agent;

/// <summary>
/// 工具结果工厂，统一构造成功/失败的 ToolResult，消除重复的消毒与构造逻辑。
/// </summary>
internal static class ToolResultFactory
{
    /// <summary>
    /// 构造错误结果（无输入数据）。
    /// </summary>
    public static ToolResult Error(LlmToolCall toolCall, string message)
    {
        var output = ResultSanitizer.Sanitize(toolCall.Name, message);
        return new ToolResult(toolCall.Id, toolCall.Name, null, output, false, message);
    }

    /// <summary>
    /// 构造错误结果（携带输入数据）。
    /// </summary>
    public static ToolResult Error(LlmToolCall toolCall, object? input, string message)
    {
        var output = ResultSanitizer.Sanitize(toolCall.Name, message);
        return new ToolResult(toolCall.Id, toolCall.Name, input, output, false, message);
    }

    /// <summary>
    /// 构造成功结果。
    /// </summary>
    public static ToolResult Success(LlmToolCall toolCall, object? input, object output)
    {
        return new ToolResult(toolCall.Id, toolCall.Name, input, output, true, null);
    }

    /// <summary>
    /// 根据节点执行结果构造 ToolResult：成功时提取输出数据，失败时构造错误信息。
    /// </summary>
    public static ToolResult FromExecutionResult(LlmToolCall toolCall, object? input, NodeExecutionResult result)
    {
        if (!result.Success)
        {
            var message = $"Tool execution failed: {result.Error?.Message ?? "Unknown error"}";
            return Error(toolCall, input, message);
        }

        object? output;
        if (result.Output.Items.Count > 0)
        {
            var data = result.Output.Items[0].Data;
            output = data is not null ? data.ToJsonString() : "Tool executed successfully.";
        }
        else
        {
            output = "Tool executed successfully.";
        }

        return Success(toolCall, input, output);
    }
}
