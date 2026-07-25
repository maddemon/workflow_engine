using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流→凭据引用关系回填。
/// 用于迁移后补齐 <c>workflow_credential_usages</c> 表中已存在工作流的引用行。
/// 仅处理「当前没有任何引用行」的工作流，已回填的工作流在后续启动时被廉价跳过。
/// </summary>
public sealed class WorkflowCredentialUsageBackfill(FlowEngineDbContext dbContext)
{
    private const int BatchSize = 200;

    /// <summary>
    /// 为尚未生成引用行的工作流补齐 <c>workflow_credential_usages</c>。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已回填的工作流数量。</returns>
    public async Task<int> BackfillAsync(CancellationToken cancellationToken = default)
    {
        var backfilled = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            // 找出尚未有任何引用行的工作流（一批）；已回填的因存在引用行而被排除。
            var batch = await dbContext.Workflows
                .AsNoTracking()
                .Where(w => !dbContext.WorkflowCredentialUsages.Any(u => u.WorkflowId == w.Id))
                .Select(w => new { w.Id, w.Name, w.Nodes })
                .Take(BatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (batch.Count == 0)
            {
                break;
            }

            var added = 0;
            foreach (var w in batch)
            {
                foreach (var usage in CredentialReferenceScanner.Scan(w.Id, w.Name, w.Nodes))
                {
                    dbContext.WorkflowCredentialUsages.Add(usage);
                    added++;
                }
            }

            // 零引用工作流（不含任何凭据引用）本就无需引用行，扫描后 added==0。
            // 若不在此终止，这些工作流因始终「无引用行」而在下一轮被反复选中，导致死循环。
            if (added == 0)
            {
                break;
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            backfilled += batch.Count;
        }

        return backfilled;
    }
}
