using System.Linq.Expressions;
using FlowEngine.Application.Triggers;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Application.Projects;

/// <summary>
/// 级联软删项目关联数据。
/// </summary>
public sealed class ProjectCascadeDeleter(
    FlowEngineDbContext dbContext,
    TriggerService triggerService,
    ILogger<ProjectCascadeDeleter> logger)
{
    public async Task CascadeSoftDeleteAsync(Guid projectId, DateTime now, CancellationToken ct)
    {
        // GAP 1（D-1 / EX-3 一致性）：级联软删前先注销该项目触发器的外部 Quartz 调度，
        // 否则工作流被软删后 ExecutionService 加载为 null 而调度残留、静默 no-op。
        // 用 try/catch 兜底：注销失败仅告警，不阻断项目级联软删（调度键为 trigger.Id，与 DB 是否已软删无关）。
        try
        {
            await triggerService.UnregisterProjectSchedulesAsync(projectId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "项目 {ProjectId} 级联软删前注销触发器调度失败，数据库将软删但调度可能残留，需人工清理。",
                projectId);
        }

        await SoftDeleteAsync(dbContext.Workflows, w => w.ProjectId == projectId && !w.Deleted, now, ct);
        await SoftDeleteAsync(dbContext.Triggers, t => t.ProjectId == projectId && !t.Deleted, now, ct);
        await SoftDeleteAsync(dbContext.ExecutionRecords, e => e.ProjectId == projectId && !e.Deleted, now, ct);
        await SoftDeleteAsync(dbContext.StoredFiles, f => f.ProjectId == projectId && !f.Deleted, now, ct);
        await SoftDeleteAsync(dbContext.Credentials, c => c.ProjectId == projectId && !c.Deleted, now, ct);
    }

    private static async Task SoftDeleteAsync<T>(
        DbSet<T> set,
        Expression<Func<T, bool>> filter,
        DateTime now,
        CancellationToken ct) where T : Entity
    {
        var items = await set.Where(filter).ToListAsync(ct);
        foreach (var item in items)
        {
            item.Deleted = true;
            item.UpdatedAt = now;
        }
    }
}
