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

            foreach (var w in batch)
            {
                foreach (var usage in CredentialReferenceScanner.Scan(w.Id, w.Name, w.Nodes))
                {
                    dbContext.WorkflowCredentialUsages.Add(usage);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            backfilled += batch.Count;
        }

        return backfilled;
    }
}
