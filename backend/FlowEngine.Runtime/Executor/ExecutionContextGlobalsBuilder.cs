using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 构建执行上下文的全局变量字典。
/// BuildFull：完整集，含逐项变量（$json/$input/$items/$runIndex/$itemIndex），$node 已填充。
/// BuildBase：基础集，不含逐项变量，$node 为空，供节点逐项求值时复用。
/// </summary>
internal static class ExecutionContextGlobalsBuilder
{
    /// <summary>
    /// 构建完整全局变量字典（含逐项变量）。
    /// </summary>
    public static Dictionary<string, object?> BuildFull(
        Dictionary<string, object?> credentialsDict,
        Dictionary<string, object?> workflowDict,
        Dictionary<string, object?> executionDict,
        Dictionary<string, object?> ctxDict,
        Dictionary<string, NodeOutput> nodeDict,
        object? currentInput,
        InputContainer inputContainer,
        List<object?> inputItems,
        IReadOnlyDictionary<string, DataBatch> latestBatches,
        int runIndex,
        IReadOnlySet<string> environmentWhitelist)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["$json"] = currentInput,
            ["$input"] = inputContainer,
            ["$items"] = new Func<string?, object?>(nodeName =>
            {
                if (string.IsNullOrEmpty(nodeName))
                    return inputItems;
                if (latestBatches.TryGetValue(nodeName, out var batch))
                    return batch.Items.Select(i => (object?)i.Data).ToList();
                return null;
            }),
            ["$node"] = nodeDict,
            ["$workflow"] = workflowDict,
            ["$execution"] = executionDict,
            ["$env"] = new EnvironmentAccessor(environmentWhitelist),
            ["$vars"] = new Dictionary<string, object?>(),
            ["$now"] = DateTime.UtcNow,
            ["$today"] = DateTime.UtcNow.Date,
            ["$runIndex"] = runIndex,
            ["$itemIndex"] = runIndex,
            ["$credentials"] = credentialsDict,
            ["$ctx"] = ctxDict,
        };
    }

    /// <summary>
    /// 构建非逐项全局变量字典，供节点在逐项求值时复用。
    /// 不含 $json/$input/$itemIndex/$runIndex（逐项变量由各节点自行注入）。
    /// </summary>
    public static Dictionary<string, object?> BuildBase(
        Dictionary<string, object?> credentialsDict,
        Workflow workflow,
        Guid executionId,
        IReadOnlyDictionary<string, object> rawParameters,
        IReadOnlySet<string> environmentWhitelist)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["$credentials"] = credentialsDict,
            ["$workflow"] = new Dictionary<string, object?>
            {
                ["id"] = workflow.Id,
                ["name"] = workflow.Name,
                ["projectId"] = workflow.ProjectId,
                ["version"] = workflow.Version,
                ["isActive"] = workflow.IsActive,
            },
            ["$execution"] = new Dictionary<string, object?>
            {
                ["id"] = executionId,
            },
            ["$env"] = new EnvironmentAccessor(environmentWhitelist),
            ["$vars"] = new Dictionary<string, object?>(),
            ["$now"] = DateTime.UtcNow,
            ["$today"] = DateTime.UtcNow.Date,
            ["$node"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            ["$ctx"] = new Dictionary<string, object?>
            {
                ["$credentials"] = credentialsDict,
                ["parameter"] = rawParameters,
            },
            ["parameter"] = rawParameters,
        };
    }
}
