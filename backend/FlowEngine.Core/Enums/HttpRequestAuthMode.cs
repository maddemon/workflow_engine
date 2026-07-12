using System.ComponentModel;

namespace FlowEngine.Core.Enums;

/// <summary>
/// HTTP 请求认证方式。
/// </summary>
public enum HttpRequestAuthMode
{
    /// <summary>无认证</summary>
    [Description("None")]
    None = 0,

    /// <summary>Bearer Token</summary>
    [Description("Bearer Token")]
    BearerToken = 1,

    /// <summary>API Key</summary>
    [Description("API Key")]
    ApiKey = 2,

    /// <summary>Basic Auth</summary>
    [Description("Basic Auth")]
    BasicAuth = 3,

    /// <summary>Query Parameter</summary>
    [Description("Query Parameter")]
    QueryParameter = 4
}
