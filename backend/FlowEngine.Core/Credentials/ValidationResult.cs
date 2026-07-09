namespace FlowEngine.Core.Credentials;

/// <summary>
/// 凭据字段校验结果。
/// </summary>
public sealed class ValidationResult
{
    /// <summary>
    /// 是否通过校验。
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// 错误信息。
    /// </summary>
    public string ErrorMessage { get; }

    private ValidationResult(bool isValid, string errorMessage)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// 创建成功结果。
    /// </summary>
    public static ValidationResult Success() => new(true, string.Empty);

    /// <summary>
    /// 创建失败结果。
    /// </summary>
    public static ValidationResult Failure(string errorMessage) => new(false, errorMessage ?? throw new ArgumentNullException(nameof(errorMessage)));
}
