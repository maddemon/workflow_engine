namespace FlowEngine.Application.Dtos;

/// <summary>
/// 注册请求。
/// </summary>
public sealed record RegisterRequest
{
    /// <summary>
    /// 邮箱地址。
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// 用户名。
    /// </summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// 密码。
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string? DisplayName { get; init; }
}

/// <summary>
/// 注册结果。
/// </summary>
public sealed record RegisterResult
{
    /// <summary>
    /// 是否成功。
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 新建用户的 ID。
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// 错误信息（失败时非空）。
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// 注册错误类型常量。
/// </summary>
public static class RegisterResultErrors
{
    /// <summary>
    /// 邮箱已被注册。
    /// </summary>
    public const string EmailAlreadyExists = "邮箱已被注册";
}

/// <summary>
/// 登录请求。
/// </summary>
public sealed record LoginRequest
{
    /// <summary>
    /// 邮箱地址。
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// 密码。
    /// </summary>
    public string Password { get; init; } = string.Empty;
}

/// <summary>
/// 登录结果。
/// </summary>
public sealed record LoginResult
{
    /// <summary>
    /// 是否成功。
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// JWT Token（成功时非空）。
    /// </summary>
    public string? Token { get; init; }

    /// <summary>
    /// 用户 ID（成功时非空）。
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// 错误信息（失败时非空）。
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// 用户信息 DTO。
/// </summary>
public sealed record UserDto
{
    /// <summary>
    /// 用户 ID。
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// 邮箱地址。
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// 用户名。
    /// </summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// 是否激活。
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// 最后更新时间。
    /// </summary>
    public DateTime? UpdatedAt { get; init; }
}
