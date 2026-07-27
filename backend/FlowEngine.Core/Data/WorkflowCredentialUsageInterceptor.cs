using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FlowEngine.Core.Data;

/// <summary>
/// 在 <see cref="FlowEngineDbContext"/> 保存时集中维护 <see cref="WorkflowCredentialUsage"/> 关联表。
/// <para>
/// 该拦截器运行于 SaveChanges 事务内部（无论事务由调用方显式开启，还是由 EF 隐式开启），
/// 因此凭据引用行的删除与写入同工作流主体数据在<strong>同一事务内原子提交</strong>，
/// 解决了原 <c>SaveChangesAsync</c> 覆盖中先以 <c>ExecuteDeleteAsync</c> 提前提交、再调用
/// <c>base.SaveChangesAsync</c> 导致非原子的问题（即 B1 事务隔离风险）。
/// </para>
/// <para>
/// 维护逻辑仅操作 <see cref="DbContext.ChangeTracker"/>（<c>RemoveRange</c> 已存在行 +
/// <c>Add</c> 新扫描行），随同本次 SaveChanges 一起提交，对所有提供程序（关系型与 InMemory）均原子。
/// 保持增量语义：仅处理本次变更的工作流；删除的工作流只移除旧引用，不重新写入。
/// </para>
/// </summary>
public sealed class WorkflowCredentialUsageInterceptor : SaveChangesInterceptor
{
    /// <summary>
    /// 在工作流数据提交前维护凭据引用行。
    /// </summary>
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is FlowEngineDbContext dbContext)
        {
            await MaintainWorkflowCredentialUsagesAsync(dbContext, cancellationToken).ConfigureAwait(false);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    private static async Task MaintainWorkflowCredentialUsagesAsync(
        FlowEngineDbContext dbContext, CancellationToken cancellationToken)
    {
        var changedWorkflows = dbContext.ChangeTracker.Entries<Workflow>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (changedWorkflows.Count == 0)
        {
            return;
        }

        var workflowIds = changedWorkflows.Select(e => e.Entity.Id).ToList();

        // 通过 ChangeTracker 删除已存在的引用行（受追踪的删除，随本次 SaveChanges 在同一事务提交），
        // 不使用 ExecuteDeleteAsync——后者会立即独立提交，破坏原子性且在 InMemory 提供程序上不受支持。
        var existing = await dbContext.WorkflowCredentialUsages
            .Where(u => workflowIds.Contains(u.WorkflowId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing.Count > 0)
        {
            dbContext.WorkflowCredentialUsages.RemoveRange(existing);
        }

        // 仅为仍存在的（新增/修改）工作流重新计算并写入引用行；删除的工作流仅清理旧引用。
        foreach (var entry in changedWorkflows)
        {
            if (entry.State == EntityState.Deleted)
            {
                continue;
            }

            foreach (var usage in CredentialReferenceScanner.Scan(entry.Entity))
            {
                dbContext.WorkflowCredentialUsages.Add(usage);
            }
        }
    }
}
