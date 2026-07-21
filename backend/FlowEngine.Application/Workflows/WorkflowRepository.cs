using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流数据访问，封装跨服务重复的查询逻辑。
/// </summary>
public sealed class WorkflowRepository(FlowEngineDbContext dbContext)
{
    /// <summary>
    /// 查找引用指定凭据的工作流名称列表。
    /// </summary>
    /// <remarks>
    /// 直接查询归一化的 <c>workflow_credential_usages</c> 关联表（按 <c>credential_id</c> 过滤），
    /// 不再全表加载工作流 JSON 列；引用行由 <see cref="FlowEngineDbContext.SaveChangesAsync"/> 集中维护。
    /// </remarks>
    public async Task<List<string>> FindReferencingCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
    {
        return await dbContext.WorkflowCredentialUsages
            .AsNoTracking()
            .Where(u => u.CredentialId == credentialId)
            .Select(u => u.WorkflowName)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
