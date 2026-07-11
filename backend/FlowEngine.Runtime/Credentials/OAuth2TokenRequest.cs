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
    /// 授权类型，默认 client_credentials。钉钉等非标准取 token 形态通常不带此参数（置空即可）。
    /// </summary>
    public string GrantType { get; set; } = "client_credentials";

    /// <summary>
    /// 响应 JSON 中取令牌的路径，默认 access_token。
    /// </summary>
    public string? TokenPath { get; set; }

    /// <summary>
    /// HTTP 请求方法（GET/POST 等），缺省为 POST（标准 client_credentials）。
    /// 钉钉 gettoken 等使用 GET。
    /// </summary>
    public string? HttpMethod { get; set; }

    /// <summary>
    /// 参数位置：请求体（form）或 URL 查询串。缺省为请求体。
    /// </summary>
    public OAuth2ParamLocation ParamLocation { get; set; } = OAuth2ParamLocation.Body;

    /// <summary>
    /// 逻辑参数名到实际参数名的映射（如 clientId→appkey），用于适配非标准端点字段命名。
    /// key 为逻辑名（clientId/clientSecret/grant_type/scope），value 为实际发送的参数名。
    /// </summary>
    public Dictionary<string, string>? ParamNameMap { get; set; }

    /// <summary>
    /// 业务错误判定路径（如钉钉的 errcode）。配置后按该字段值判定业务成功/失败。
    /// 缺省不判定（仅按 HTTP 状态码）。
    /// </summary>
    public string? ResponseErrorPath { get; set; }

    /// <summary>
    /// 视为业务成功的字段值集合（与 <see cref="ResponseErrorPath"/> 配合）。
    /// 缺省或为空表示：只要 <see cref="ResponseErrorPath"/> 字段存在即视为成功。
    /// </summary>
    public List<string>? ResponseSuccessValues { get; set; }

    /// <summary>
    /// 额外请求参数（预留）。
    /// </summary>
    public Dictionary<string, string>? ExtraParameters { get; set; }
}

/// <summary>
/// OAuth2 请求参数位置。
/// </summary>
public enum OAuth2ParamLocation
{
    /// <summary>请求体（application/x-www-form-urlencoded）。</summary>
    Body = 0,

    /// <summary>URL 查询串（如钉钉 gettoken 的 ?appkey=&amp;appsecret=）。</summary>
    Query = 1
}
