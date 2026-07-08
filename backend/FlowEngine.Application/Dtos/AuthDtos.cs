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
    /// 用户信息（成功时非空）。
    /// </summary>
    public UserDto? User { get; init; }

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

    /// <summary>
    /// 用户角色列表。
    /// </summary>
    public IReadOnlyList<string> Roles { get; init; } = [];
}

/// <summary>
/// 分配角色请求。
/// </summary>
public sealed record AssignRoleRequest
{
    /// <summary>
    /// 角色名称（Admin/Editor/Viewer）。
    /// </summary>
    public string Role { get; init; } = string.Empty;
}

/// <summary>
/// 创建 API Key 请求。
/// </summary>
public sealed record CreateApiKeyRequest
{
    /// <summary>
    /// API Key 名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 过期时间，null 表示永不过期。
    /// </summary>
    public DateTime? ExpiresAt { get; init; }
}

/// <summary>
/// 创建 API Key 结果，仅返回一次 Key 明文。
/// </summary>
public sealed record CreateApiKeyResult
{
    /// <summary>
    /// API Key ID。
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// API Key 名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Key 前缀。
    /// </summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>
    /// 过期时间。
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// Key 明文（仅返回一次）。
    /// </summary>
    public string Key { get; init; } = string.Empty;
}

/// <summary>
/// API Key 列表项 DTO（不包含明文）。
/// </summary>
public sealed record ApiKeyDto
{
    /// <summary>
    /// API Key ID。
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// API Key 名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Key 前缀。
    /// </summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// 过期时间。
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// 吊销时间。
    /// </summary>
    public DateTime? RevokedAt { get; init; }
}
