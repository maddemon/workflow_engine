using System.Collections.Generic;

namespace FlowEngine.Runtime.Credentials;

/// <summary>
/// OAuth2 provider 内置策略模板。
/// 用户创建 oauth2 凭据时只需填 clientId/clientSecret/tokenUrl（+可选 provider），
/// 引擎按 provider 套用对应内置 <see cref="OAuth2TokenRequest"/> 策略，避免用户手填陌生的请求方法/参数名/错误码。
/// </summary>
public static class OAuth2ProviderTemplates
{
    /// <summary>通用标准 client_credentials（POST + form + client_id/client_secret）。</summary>
    public const string Standard = "standard";

    /// <summary>钉钉 gettoken：GET + query appkey/appsecret + errcode==0 判定。</summary>
    public const string Dingtalk = "dingtalk";

    /// <summary>
    /// 按 provider 名称套用内置策略模板，返回填充好策略字段的 <paramref name="request"/>。
    /// 未知/空 provider 视为 standard，不做任何覆盖。
    /// </summary>
    public static OAuth2TokenRequest Apply(OAuth2TokenRequest request, string? provider)
    {
        if (string.Equals(provider, Dingtalk, System.StringComparison.OrdinalIgnoreCase))
        {
            // 钉钉 gettoken：GET ?appkey=&appsecret=，响应 errcode==0 视为成功
            request.HttpMethod = "GET";
            request.ParamLocation = OAuth2ParamLocation.Query;
            request.ParamNameMap = new Dictionary<string, string>(System.StringComparer.Ordinal)
            {
                ["clientId"] = "appkey",
                ["clientSecret"] = "appsecret"
            };
            request.ResponseErrorPath = "errcode";
            request.ResponseSuccessValues = new List<string> { "0" };
            // 钉钉 gettoken 不带 grant_type
            request.GrantType = string.Empty;
        }

        return request;
    }
}
