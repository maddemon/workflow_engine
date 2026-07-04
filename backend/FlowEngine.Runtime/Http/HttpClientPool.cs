using FlowEngine.Core.Abstractions;

namespace FlowEngine.Runtime.Http;

/// <summary>
/// 基于 IHttpClientFactory 的 HTTP 客户端连接池。
/// </summary>
public class HttpClientPool : IHttpClientPool
{
    private readonly IHttpClientFactory _factory;

    public HttpClientPool(IHttpClientFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public HttpClient GetClient(string? name = null)
    {
        return string.IsNullOrEmpty(name)
            ? _factory.CreateClient()
            : _factory.CreateClient(name);
    }
}
