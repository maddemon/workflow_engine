namespace FlowEngine.Core.Entities;

/// <summary>
/// 节点执行结果。
/// </summary>
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
}
