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
    public async Task<List<string>> FindReferencingCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
    {
        var credentialIdStr = credentialId.ToString();

        // 工作流数量有限，直接加载后在内存中精确匹配。
        var allWorkflows = await dbContext.Workflows
            .Select(w => new { w.Id, w.Name, w.Nodes })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return allWorkflows
            .Where(w => WorkflowReferencesCredential(w.Nodes, credentialIdStr))
            .Select(w => w.Name)
            .ToList();
    }

    private static bool WorkflowReferencesCredential(List<NodeDefinition> nodes, string credentialId)
    {
        foreach (var node in nodes)
        {
            foreach (var paramValue in node.Parameters.Values)
            {
                if (paramValue is string strValue && strValue.Equals(credentialId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
