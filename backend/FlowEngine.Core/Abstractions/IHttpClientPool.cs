namespace FlowEngine.Core.Abstractions;

/// <summary>
/// HTTP 客户端连接池。
/// </summary>
public interface IHttpClientPool
{
    /// <summary>
    /// 获取一个 HttpClient 实例。
    /// </summary>
    /// <param name="name">客户端名称（用于分类配置）。</param>
    HttpClient GetClient(string? name = null);
}
