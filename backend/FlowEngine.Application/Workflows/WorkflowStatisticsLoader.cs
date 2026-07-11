using FlowEngine.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 加载工作流统计数据（最后执行时间、触发器数量、下次触发时间）。
/// </summary>
public sealed class WorkflowStatisticsLoader(FlowEngineDbContext dbContext)
{
    /// <summary>
    /// 批量加载指定工作流的统计数据，避免逐行 N+1 查询。
    /// </summary>
    /// <param name="workflowIds">需要统计的工作流 ID 列表。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>工作流 ID 到统计信息的字典；未命中条目表示无统计数据。</returns>
    internal async Task<Dictionary<Guid, WorkflowStats>> LoadAsync(
        IReadOnlyList<Guid> workflowIds,
        CancellationToken ct)
    {
        if (workflowIds.Count == 0)
        {
            return new Dictionary<Guid, WorkflowStats>();
        }

        // BE-01: 批量查询关联数据，避免每行 N+1。
        var lastExecutions = await dbContext.ExecutionRecords
            .Where(e => workflowIds.Contains(e.WorkflowDefinitionId) && e.CompletedAt != null)
            .GroupBy(e => e.WorkflowDefinitionId)
            .Select(g => new { WorkflowId = g.Key, LastCompletedAt = g.Max(e => e.CompletedAt) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var triggerStats = await dbContext.Triggers
            .Where(t => workflowIds.Contains(t.WorkflowDefinitionId) && !t.Deleted)
            .GroupBy(t => t.WorkflowDefinitionId)
            .Select(g => new { WorkflowId = g.Key, Count = g.Count(), NextTriggerAt = g.Min(t => t.NextTriggerAt) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var lastExecMap = lastExecutions.ToDictionary(x => x.WorkflowId, x => x.LastCompletedAt);
        var triggerMap = triggerStats.ToDictionary(x => x.WorkflowId);

        var result = new Dictionary<Guid, WorkflowStats>(workflowIds.Count);
        foreach (var id in workflowIds)
        {
            var lastExec = lastExecMap.GetValueOrDefault(id);
            var triggerStat = triggerMap.GetValueOrDefault(id);
            result[id] = new WorkflowStats(
                lastExec,
                triggerStat?.Count ?? 0,
                triggerStat?.NextTriggerAt);
        }

        return result;
    }
}

/// <summary>
/// 工作流统计信息（最后执行时间、触发器数量、下次触发时间）。
/// </summary>
public sealed record WorkflowStats(
    DateTime? LastExecutionAt,
    int TriggerCount,
    DateTime? NextTriggerAt);
