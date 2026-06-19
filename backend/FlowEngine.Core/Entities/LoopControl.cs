namespace FlowEngine.Core.Entities;

/// <summary>
/// 循环控制状态。
/// </summary>
public class LoopControl
{
    /// <summary>
    /// 是否继续循环。
    /// </summary>
    public bool Continue { get; set; }

    /// <summary>
    /// 当前迭代索引。
    /// </summary>
    public int IterationIndex { get; set; }

    /// <summary>
    /// 下一项数据。
    /// </summary>
    public object? NextItem { get; set; }
}
