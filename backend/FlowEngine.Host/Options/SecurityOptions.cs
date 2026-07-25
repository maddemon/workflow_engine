namespace FlowEngine.Host.Options;

/// <summary>
/// 安全加固相关配置（SEC-2 / S-4）。
/// </summary>
public sealed class SecurityOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "Security";

    /// <summary>
    /// 是否启用全局鉴权兜底（FallbackPolicy = RequireAuthenticatedUser）。
    /// 启用后，任何未显式标注 <c>[AllowAnonymous]</c> 的端点默认要求已认证用户；
    /// 合法匿名端点（健康检查、Webhook 接收、登录等）必须显式标注 <c>[AllowAnonymous]</c>。
    /// 默认 <c>true</c>（安全默认）。
    /// </summary>
    public bool RequireAuthenticatedUserByDefault { get; set; } = true;

    /// <summary>
    /// 是否启用针对 Cookie 认证的 CSRF 防护（S-4）。
    /// 仅对携带 <c>fe_auth</c> Cookie 的变更请求（POST/PUT/DELETE/PATCH）要求
    /// 自定义防伪造请求头（<see cref="CsrfHeaderName"/>），缺失即拒绝（403）。
    /// Bearer / API Key 请求与匿名请求不受影响。默认 <c>true</c>。
    /// </summary>
    public bool EnableCsrfProtection { get; set; } = true;

    /// <summary>
    /// CSRF 防护要求的请求头名称。
    /// </summary>
    public string CsrfHeaderName { get; set; } = "X-Requested-With";

    /// <summary>
    /// CSRF 防护要求的请求头取值（任意非空前端的固定值即可，跨站请求无法设置）。
    /// </summary>
    public string CsrfHeaderValue { get; set; } = "FlowEngine";
}
