using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using FlowEngine.Core.Http;

namespace FlowEngine.Core.Tests;

/// <summary>
/// <see cref="SsrfGuard.CreateConnectCallback"/> 行为测试：验证在建立 TCP 连接前对每个解析出的 IP 做内部/保留地址校验，
/// 内部地址被拦截并按「失败关闭」抛 <see cref="InvalidOperationException"/>，外部地址放行并尝试建连。
/// </summary>
public class SsrfGuardConnectCallbackTests
{
    /// <summary>
    /// 构造连接上下文。SocketsHttpConnectionContext 在 .NET 运行时的构造函数为非公开，
    /// 测试通过反射调用其 internal 构造函数（DnsEndPoint, HttpRequestMessage）以驱动回调。
    /// </summary>
    private static SocketsHttpConnectionContext BuildContext(string host, int port)
    {
        var ctor = typeof(SocketsHttpConnectionContext).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(DnsEndPoint), typeof(HttpRequestMessage)],
            null)
            ?? throw new InvalidOperationException("无法定位 SocketsHttpConnectionContext 构造函数");
        return (SocketsHttpConnectionContext)ctor.Invoke(
            [new DnsEndPoint(host, port), new HttpRequestMessage(HttpMethod.Get, "http://localhost/")]);
    }

    [Fact]
    public async Task CreateConnectCallback_InternalLiteralIp_ThrowsBlocked()
    {
        var callback = SsrfGuard.CreateConnectCallback();
        var context = BuildContext("127.0.0.1", 80);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => callback(context, CancellationToken.None).AsTask());

        Assert.Contains("内部/保留地址", ex.Message);
    }

    [Fact]
    public async Task CreateConnectCallback_InternalIpv6Literal_ThrowsBlocked()
    {
        var callback = SsrfGuard.CreateConnectCallback();
        var context = BuildContext("::1", 80);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => callback(context, CancellationToken.None).AsTask());

        Assert.Contains("内部/保留地址", ex.Message);
    }

    [Fact]
    public async Task CreateConnectCallback_ExternalLiteralIp_PassesGuardAndAttemptsConnect()
    {
        // 1.2.3.4 为公开地址（非内部/保留），解析后通过校验，进入真实 TCP 建连。
        // 由于该地址无监听端口，建连失败并抛出（但不应是「内部/保留地址」拦截消息）。
        var callback = SsrfGuard.CreateConnectCallback();
        var context = BuildContext("1.2.3.4", 9);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => callback(context, cts.Token).AsTask());

        Assert.DoesNotContain("内部/保留地址", ex.Message);
        Assert.DoesNotContain("DNS 解析失败", ex.Message);
    }
}
