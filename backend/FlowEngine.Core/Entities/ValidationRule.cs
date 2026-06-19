namespace FlowEngine.Core.Entities;

/// <summary>
/// 验证规则。
/// </summary>
public class ValidationRule
{
    /// <summary>
    /// 规则类型。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 规则值。
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// 错误提示信息。
    /// </summary>
    public string? ErrorMessage { get; set; }
}
