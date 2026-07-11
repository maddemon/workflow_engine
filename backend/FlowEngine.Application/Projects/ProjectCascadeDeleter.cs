using System.Linq.Expressions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Projects;

/// <summary>
/// 级联软删项目关联数据。
/// </summary>
public sealed class ProjectCascadeDeleter(FlowEngineDbContext dbContext)
{
    public async Task CascadeSoftDeleteAsync(Guid projectId, DateTime now, CancellationToken ct)
    {
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
