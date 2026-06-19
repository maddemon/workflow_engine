namespace FlowEngine.Core.Entities;

/// <summary>
/// 显示规则。
/// </summary>
public class DisplayRule
{
    /// <summary>
    /// 显示条件表达式。
    /// </summary>
    public string Condition { get; set; } = string.Empty;

    /// <summary>
    /// 依赖字段列表。
    /// </summary>
    public List<string> Dependencies { get; set; } = [];
}
