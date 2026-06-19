namespace FlowEngine.Core.Entities;

/// <summary>
/// 选项。
/// </summary>
public class Option
{
    /// <summary>
    /// 显示标签。
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// 选项值。
    /// </summary>
    public object Value { get; set; } = null!;
}
