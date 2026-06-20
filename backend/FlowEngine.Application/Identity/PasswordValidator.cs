using System.Text.RegularExpressions;

namespace FlowEngine.Application.Identity;

/// <summary>
/// 密码强度校验器，实现默认密码策略。
/// 默认策略：最小长度 8，至少一个大写字母、一个小写字母、一个数字、一个特殊字符。
/// </summary>
public partial class PasswordValidator : IPasswordValidator
{
    /// <summary>
    /// 最小密码长度。
    /// </summary>
    public int MinLength { get; init; } = 8;

    /// <summary>
    /// 初始化密码校验器，使用默认策略。
    /// </summary>
    public PasswordValidator()
    {
    }

    /// <summary>
    /// 初始化密码校验器，自定义最小长度。
    /// </summary>
    /// <param name="minLength">最小密码长度。</param>
    public PasswordValidator(int minLength)
    {
        MinLength = minLength;
    }

    /// <inheritdoc />
    public (bool IsValid, string? ErrorMessage) Validate(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return (false, "密码不能为空");
        }

        if (password.Length < MinLength)
        {
            return (false, $"密码长度至少为 {MinLength} 个字符");
        }

        if (!UppercaseRegex().IsMatch(password))
        {
            return (false, "密码必须包含至少一个大写字母");
        }

        if (!LowercaseRegex().IsMatch(password))
        {
            return (false, "密码必须包含至少一个小写字母");
        }

        if (!DigitRegex().IsMatch(password))
        {
            return (false, "密码必须包含至少一个数字");
        }

        if (!SpecialCharRegex().IsMatch(password))
        {
            return (false, "密码必须包含至少一个特殊字符");
        }

        return (true, null);
    }

    [GeneratedRegex(@"[A-Z]")]
    private static partial Regex UppercaseRegex();

    [GeneratedRegex(@"[a-z]")]
    private static partial Regex LowercaseRegex();

    [GeneratedRegex(@"[0-9]")]
    private static partial Regex DigitRegex();

    [GeneratedRegex(@"[^a-zA-Z0-9]")]
    private static partial Regex SpecialCharRegex();
}
