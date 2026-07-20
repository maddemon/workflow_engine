using System.Text.Json;
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
    /// 当前实现将 Nodes JSON 列投影后在内存中过滤，以兼容 EF Core 对 JSON 列反序列化后参数值为 JsonElement 的情况。
    /// TODO：当工作流数量增长时，应考虑将凭据引用关系抽取到独立关联表或建立 JSON 列索引，避免全表加载。
    /// </remarks>
    public async Task<List<string>> FindReferencingCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
    {
        var credentialIdStr = credentialId.ToString();

        // 只读查询加 AsNoTracking；投影仅加载 Id/Name/Nodes 以减少内存占用。
        var allWorkflows = await dbContext.Workflows
            .AsNoTracking()
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
                var strValue = paramValue switch
                {
                    string s => s,
                    JsonElement element => element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString(),
                    _ => paramValue?.ToString(),
                };

                if (!string.IsNullOrEmpty(strValue) && strValue.Equals(credentialId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
