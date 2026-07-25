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
    /// 多输出端口映射：端口名 -> 数据批次。用于一个节点同时向多个输出端口（如 Filter 的 Kept/Discarded）分发数据。
    /// 提供时 <see cref="OutputRouter"/> 会按各端口分别路由；未提供时回退到单一 <see cref="Output"/> + <see cref="BranchIndex"/> 逻辑
    /// （兼容 If/Switch 等只向单个端口输出的分支节点）。
    /// </summary>
    public Dictionary<string, DataBatch>? PortOutputs { get; set; }

    /// <summary>
    /// Agent/Sub-Agent 节点执行期间产生的工具调用记录（含父记录 ID 透传）。
    /// 由 Agent 类节点在执行完成后填充，便于审计与测试断言；非 Agent 节点保持空列表。
    /// </summary>
    public List<NodeExecutionRecord> ToolExecutionRecords { get; set; } = [];
}
