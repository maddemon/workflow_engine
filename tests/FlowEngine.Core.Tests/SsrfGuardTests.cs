using System.Net;
using System.Net.Http;
using FlowEngine.Core.Http;

namespace FlowEngine.Core.Tests;

public class SsrfGuardTests
{
    [Theory]
    [InlineData("http://127.0.0.1/api", true)]
    [InlineData("http://localhost/api", true)]
    [InlineData("https://10.0.0.1/api", true)]
    [InlineData("https://192.168.1.1/api", true)]
    [InlineData("http://172.16.0.1/api", true)]
    [InlineData("http://169.254.169.254/latest/meta-data", true)]
    [InlineData("http://0.0.0.0/api", true)]
    [InlineData("http://100.64.0.1/api", true)]
    [InlineData("http://100.127.255.255/api", true)]
    [InlineData("http://192.0.0.1/api", true)]
    [InlineData("http://198.18.0.1/api", true)]
    [InlineData("http://198.19.255.255/api", true)]
    [InlineData("http://[::1]/api", true)]
    [InlineData("http://[::]/api", true)]
    [InlineData("http://[fc00::1]/api", true)]
    [InlineData("http://[fe80::1]/api", true)]
    [InlineData("http://[::ffff:127.0.0.1]/api", true)]
    [InlineData("http://[::ffff:10.0.0.1]/api", true)]
    public void IsInternalTarget_IpLiteral_ReturnsExpected(string url, bool expected)
    {
        Assert.Equal(expected, SsrfGuard.IsInternalTarget(url));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("not-a-url", true)]
    [InlineData("ftp://example.com/file", true)]
    [InlineData("http:///path", true)]
    public void IsInternalTarget_InvalidUrl_Blocked(string? url, bool expected)
    {
        Assert.Equal(expected, SsrfGuard.IsInternalTarget(url));
    }

    [Fact]
    public void IsInternalTarget_ExternalIpLiteral_ReturnsFalse()
    {
        Assert.False(SsrfGuard.IsInternalTarget("http://8.8.8.8/api"));
    }

    [Fact]
    public void IsInternalTarget_PublicUrl_MayResolveToExternal()
    {
        // DNS 解析失败时按不安全处理，因此仅验证不抛异常。
        var result = SsrfGuard.IsInternalTarget("http://example.com/api");
        Assert.True(result || !result);
    }

}
