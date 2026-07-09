namespace FlowEngine.Runtime.Credentials;

/// <summary>
/// OAuth2 令牌端点请求参数。
/// </summary>
public sealed class OAuth2TokenRequest
{
    /// <summary>
    /// 令牌端点地址。
    /// </summary>
    public string TokenUrl { get; set; } = string.Empty;

    /// <summary>
    /// 客户端 ID。
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// 客户端密钥。
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// 授权范围。
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// 授权类型，默认 client_credentials。
    /// </summary>
    public string GrantType { get; set; } = "client_credentials";

    /// <summary>
    /// 响应 JSON 中取令牌的路径，默认 access_token。
    /// </summary>
    public string? TokenPath { get; set; }

    /// <summary>
    /// 额外请求参数（预留）。
    /// </summary>
    public Dictionary<string, string>? ExtraParameters { get; set; }
}
