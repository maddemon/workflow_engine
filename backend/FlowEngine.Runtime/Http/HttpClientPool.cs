using System.Net.Http;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Http;

namespace FlowEngine.Runtime.Http;

/// <summary>
/// 基于 SSRF 安全 handler 的 HTTP 客户端连接池。
/// 使用 <see cref="SocketsHttpHandler"/> 配合 <see cref="SsrfGuard.CreateConnectCallback"/> 在 TCP 层 pin IP，
/// 且 <see cref="SocketsHttpHandler"/> 不自动跟随重定向，避免重定向到内部地址。
/// </summary>
public sealed class HttpClientPool : IHttpClientPool, IDisposable
{
    private readonly SocketsHttpHandler _handler;

    /// <summary>
    /// 初始化连接池，创建共享的 SSRF 安全 handler。
    /// </summary>
    public HttpClientPool()
    {
        _handler = new SocketsHttpHandler
        {
            ConnectCallback = SsrfGuard.CreateConnectCallback(),
            AllowAutoRedirect = false // 显式禁用自动重定向，防止重定向到内部地址绕过 SSRF 防护
        };
    }

    /// <inheritdoc />
    public HttpClient GetClient(string? name = null)
    {
        return new HttpClient(_handler, disposeHandler: false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _handler.Dispose();
    }
}
