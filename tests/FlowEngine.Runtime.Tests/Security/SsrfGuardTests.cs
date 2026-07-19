using FlowEngine.Core.Http;

namespace FlowEngine.Runtime.Tests.Security;

/// <summary>
/// SSRF 防护判定测试。覆盖内网/保留地址、公网地址、非法输入。
/// 离线环境下公网主机名依赖 DNS，故公网判定仅使用字面量 IP（不触发 DNS）。
/// </summary>
public class SsrfGuardTests
{
    [Fact]
    public void IsInternalTarget_Null_ReturnsTrue()
    {
        Assert.True(SsrfGuard.IsInternalTarget(null));
    }

    [Fact]
    public void IsInternalTarget_EmptyString_ReturnsTrue()
    {
        Assert.True(SsrfGuard.IsInternalTarget(string.Empty));
    }

    [Fact]
    public void IsInternalTarget_Whitespace_ReturnsTrue()
    {
        Assert.True(SsrfGuard.IsInternalTarget("   "));
    }

    [Fact]
    public void IsInternalTarget_NonAbsoluteUrl_ReturnsTrue()
    {
        // 非绝对 URI（无 scheme）无法解析，按不安全处理。
        Assert.True(SsrfGuard.IsInternalTarget("example.com"));
    }

    [Fact]
    public void IsInternalTarget_NonHttpScheme_ReturnsTrue()
    {
        // 仅允许 http/https，ftp 视为不安全。
        Assert.True(SsrfGuard.IsInternalTarget("ftp://example.com"));
    }

    [Theory]
    [InlineData("http://127.0.0.1")]            // 回环
    [InlineData("http://10.0.0.5")]             // RFC1918 10/8
    [InlineData("http://192.168.1.1")]          // RFC1918 192.168/16
    [InlineData("http://172.16.5.5")]           // RFC1918 172.16/12
    [InlineData("http://169.254.169.254")]      // 链路本地 / 云元数据
    [InlineData("http://100.64.0.1")]           // CGNAT 100.64/10
    [InlineData("http://0.0.0.0")]              // 0.0.0.0/8
    [InlineData("http://[::1]")]                // IPv6 回环
    [InlineData("http://[fc00::1]")]            // IPv6 ULA
    public void IsInternalTarget_InternalLiteralIp_ReturnsTrue(string url)
    {
        Assert.True(SsrfGuard.IsInternalTarget(url));
    }

    [Theory]
    [InlineData("http://8.8.8.8")]                          // 公网 IPv4 字面量
    [InlineData("https://1.1.1.1")]                         // 公网 IPv4 字面量
    [InlineData("http://[2606:4700:4700::1111]")]          // 公网 IPv6 字面量
    public void IsInternalTarget_PublicLiteralIp_ReturnsFalse(string url)
    {
        Assert.False(SsrfGuard.IsInternalTarget(url));
    }

    [Fact]
    public void IsInternalTarget_PublicHostname_DoesNotThrow_AndReturnsBool()
    {
        // 离线环境 DNS 解析失败，按「不安全」返回 true；仅断言不抛异常且返回 bool。
        var result = SsrfGuard.IsInternalTarget("https://example.com");
        Assert.IsType<bool>(result);
    }
}
