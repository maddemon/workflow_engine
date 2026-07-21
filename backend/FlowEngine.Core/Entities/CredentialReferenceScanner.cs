using System.Text.Json;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 凭据引用扫描器。
/// 扫描工作流节点参数，识别其值等于某个凭据 ID（Guid）的引用，生成归一化的
/// <see cref="WorkflowCredentialUsage"/> 行。供 <see cref="FlowEngineDbContext"/> 的
/// <c>SaveChangesAsync</c> 覆盖与回填任务共用，保证引用关系计算逻辑单一来源。
/// </summary>
public static class CredentialReferenceScanner
{
    /// <summary>
    /// 扫描工作流，返回其引用的凭据使用行。
    /// </summary>
    /// <param name="workflow">待扫描的工作流。</param>
    /// <returns>去重后的凭据引用行集合（按 工作流+凭据+节点 唯一）。</returns>
    public static IReadOnlyList<WorkflowCredentialUsage> Scan(Workflow workflow)
        => Scan(workflow.Id, workflow.Name, workflow.Nodes);

    /// <summary>
    /// 扫描工作流的节点参数，返回其引用的凭据使用行（用于不持有完整 <see cref="Workflow"/> 实体的场景，如回填）。
    /// </summary>
    /// <param name="workflowId">工作流 ID。</param>
    /// <param name="workflowName">工作流名称（冗余存储）。</param>
    /// <param name="nodes">工作流节点列表。</param>
    /// <returns>去重后的凭据引用行集合（按 工作流+凭据+节点 唯一）。</returns>
    public static IReadOnlyList<WorkflowCredentialUsage> Scan(
        Guid workflowId, string workflowName, IReadOnlyList<NodeDefinition> nodes)
    {
        var results = new List<WorkflowCredentialUsage>();
        var seen = new HashSet<(Guid CredentialId, string NodeId)>();

        foreach (var node in nodes)
        {
            foreach (var paramValue in node.Parameters.Values)
            {
                var strValue = ToStringValue(paramValue);
                if (string.IsNullOrEmpty(strValue) || !Guid.TryParse(strValue, out var credentialId))
                {
                    continue;
                }

                // 同一节点内重复引用同一凭据只保留一行（复合主键约束）。
                if (!seen.Add((credentialId, node.Id)))
                {
                    continue;
                }

                results.Add(new WorkflowCredentialUsage
                {
                    WorkflowId = workflowId,
                    CredentialId = credentialId,
                    WorkflowName = workflowName,
                    NodeId = node.Id,
                });
            }
        }

        return results;
    }

    private static string? ToStringValue(object? value) => value switch
    {
        null => null,
        string s => s,
        JsonElement element => element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : element.ToString(),
        _ => value.ToString(),
    };
}
