using System.Net.Http;
using FlowEngine.Runtime.Http;
using Xunit;

namespace FlowEngine.Runtime.Tests.Http;

/// <summary>
/// <see cref="HttpClientPool"/> 连接池构造、取客户端与释放覆盖测试。
/// </summary>
public class HttpClientPoolTests
{
    [Fact]
    public void GetClient_ReturnsClient_AndDispose_DoesNotThrow()
    {
        using var pool = new HttpClientPool();
        var client = pool.GetClient("default");
        Assert.NotNull(client);
        pool.Dispose();
    }

    [Fact]
    public void GetClient_WithoutName_ReturnsClient()
    {
        using var pool = new HttpClientPool();
        var client = pool.GetClient();
        Assert.NotNull(client);
    }
}
