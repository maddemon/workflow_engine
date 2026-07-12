using System.ComponentModel;

namespace FlowEngine.Core.Enums;

/// <summary>
/// HTTP 请求方法。
/// </summary>
public enum HttpMethodOption
{
    /// <summary>GET</summary>
    [Description("GET")]
    Get = 0,

    /// <summary>POST</summary>
    [Description("POST")]
    Post = 1,

    /// <summary>PUT</summary>
    [Description("PUT")]
    Put = 2,

    /// <summary>DELETE</summary>
    [Description("DELETE")]
    Delete = 3,

    /// <summary>PATCH</summary>
    [Description("PATCH")]
    Patch = 4
}
