using System.Text.RegularExpressions;

namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// 数据库标识符校验器。
/// </summary>
public static partial class IdentifierValidator
{
    [GeneratedRegex("^[A-Za-z0-9_]+$", RegexOptions.Compiled)]
    private static partial Regex IdentifierRegex();

    /// <summary>
    /// 验证标识符是否只包含字母、数字和下划线。
    /// </summary>
    public static bool IsValid(string identifier) => !string.IsNullOrEmpty(identifier) && IdentifierRegex().IsMatch(identifier);

    /// <summary>
    /// 验证标识符，非法时抛出异常。
    /// </summary>
    public static void EnsureValid(string identifier, string role)
    {
        if (!IsValid(identifier))
        {
            throw new ArgumentException($"{role} '{identifier}' 包含非法字符，仅允许字母、数字和下划线。", identifier);
        }
    }
}
