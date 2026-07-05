namespace FlowEngine.Application.Workflows;

/// <summary>
/// 工作流校验结果。
/// </summary>
/// <remarks>
/// 初始化校验结果。
/// </remarks>
/// <param name="errors">错误信息列表。</param>
public sealed class ValidationResult(IEnumerable<string>? errors = null)
{

    /// <summary>
    /// 是否通过校验。
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// 错误信息列表。
    /// </summary>
    public IReadOnlyList<string> Errors { get; } = (errors ?? Array.Empty<string>()).ToList().AsReadOnly();
}
