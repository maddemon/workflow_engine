using System.Text.Json.Nodes;

namespace FlowEngine.Runtime.Credentials;

/// <summary>
/// OAuth2 令牌端点响应。
/// </summary>
public sealed class OAuth2TokenResponse
{
    /// <summary>
    /// 访问令牌。
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// 令牌类型，默认 Bearer。
    /// </summary>
    public string? TokenType { get; set; } = "Bearer";

    /// <summary>
    /// 过期时间（秒）。
    /// </summary>
    public long? ExpiresIn { get; set; }

    /// <summary>
    /// 刷新令牌。
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// 授权范围。
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// 原始响应 JSON。
    /// </summary>
    public JsonNode? Raw { get; set; }
}
