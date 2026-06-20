namespace FlowEngine.Application.Identity;

/// <summary>
/// 密码强度校验接口。
/// </summary>
public interface IPasswordValidator
{
    /// <summary>
    /// 校验密码强度。
    /// </summary>
    /// <param name="password">待校验的密码。</param>
    /// <returns>校验结果（是否有效 + 错误信息）。</returns>
    (bool IsValid, string? ErrorMessage) Validate(string password);
}
