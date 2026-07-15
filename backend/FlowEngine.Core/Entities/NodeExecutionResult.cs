using System.ComponentModel.DataAnnotations.Schema;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 节点执行结果。
/// </summary>
[NotMapped]
public class NodeExecutionResult
{
    /// <summary>
    /// 是否执行成功。
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 输出数据批次。
    /// </summary>
    public DataBatch Output { get; set; } = new();

    /// <summary>
    /// 错误信息。
    /// </summary>
    public NodeError? Error { get; set; }

    /// <summary>
    /// 分支索引。
    /// </summary>
    public int? BranchIndex { get; set; }

    /// <summary>
    /// Agent/Sub-Agent 节点执行期间产生的工具调用记录（含父记录 ID 透传）。
    /// 由 Agent 类节点在执行完成后填充，便于审计与测试断言；非 Agent 节点保持空列表。
    /// </summary>
    public List<NodeExecutionRecord> ToolExecutionRecords { get; set; } = [];
}
