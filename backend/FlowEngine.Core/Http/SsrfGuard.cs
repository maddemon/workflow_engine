using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FlowEngine.Core.Http;

/// <summary>
/// SSRF 防护：拦截指向内网/保留地址的 HTTP 请求目标，防止工作流访问云元数据服务或内部网络。
/// </summary>
public static class SsrfGuard
{
    /// <summary>
    /// 同步预检给定 URL 是否指向被禁止的内部/保留地址（字面量 IP 直接判定；主机名做一次性 DNS 解析）。
    /// 解析失败时按「不安全」处理（返回 true）。
    /// 注意：本方法是「尽力而为」的同步预检，不防 DNS 重绑定；经 HTTP 客户端发出的请求应以
    /// <see cref="CreateConnectCallback"/> 在连接瞬间对实际解析 IP 做的校验作为权威防护。
    /// </summary>
    public static bool IsInternalTarget(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return true;
        }

        // 仅允许 http/https 方案。
        if (uri.Scheme is not ("http" or "https"))
        {
            return true;
        }

        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var literalIp))
        {
            return IsInternalAddress(literalIp);
        }

        try
        {
            var addresses = Dns.GetHostAddresses(host);
            foreach (var address in addresses)
            {
                if (IsInternalAddress(address))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception)
        {
            // 解析失败按不安全处理，避免 DNS 重绑定绕过。
            return true;
        }
    }

    /// <summary>
    /// 创建 SSRF 安全的 <see cref="SocketsHttpHandler.ConnectCallback"/>。
    /// 在 TCP 连接前解析主机名并 pin 住具体 IP，校验每个 IP 是否为内部/保留地址，
    /// 通过则用该 IP 建立连接，避免 DNS 重绑定绕过。
    /// </summary>
    /// <returns>可用于 <see cref="SocketsHttpHandler.ConnectCallback"/> 的回调。</returns>
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> CreateConnectCallback()
    {
        return async (context, cancellationToken) =>
        {
            var host = context.DnsEndPoint.Host;
            var port = context.DnsEndPoint.Port;

            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                throw new InvalidOperationException($"SSRF 防护：DNS 解析失败 {host}");
            }

            if (addresses.Length == 0)
            {
                throw new InvalidOperationException($"SSRF 防护：未解析到地址 {host}");
            }

            // 在连接瞬间逐一校验解析出的实际 IP（防 DNS 重绑定：预解析结果与此处可能不同）。
            foreach (var address in addresses)
            {
                if (IsInternalAddress(address))
                {
                    throw new InvalidOperationException($"SSRF 防护：目标地址 {address} 为内部/保留地址，已被拦截");
                }
            }

            // pin 第一个非内部 IP 建立 TCP 连接；连接前再次校验该实际 IP，确保不被绕过。
            var connectAddress = addresses[0];
            if (IsInternalAddress(connectAddress))
            {
                throw new InvalidOperationException($"SSRF 防护：目标地址 {connectAddress} 为内部/保留地址，已被拦截");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(connectAddress, port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
    }

    private static bool IsInternalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();

        // IPv4
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            // 0.0.0.0/8
            if (bytes[0] == 0)
            {
                return true;
            }

            // 10.0.0.0/8 (RFC1918)
            if (bytes[0] == 10)
            {
                return true;
            }

            // 100.64.0.0/10 (CGNAT)
            if (bytes[0] == 100 && (bytes[1] & 0xC0) == 0x40)
            {
                return true;
            }

            // 127.0.0.0/8 (loopback, 已含 IsLoopback 但保险)
            if (bytes[0] == 127)
            {
                return true;
            }

            // 169.254.0.0/16 (链路本地，含 169.254.169.254 云元数据)
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }

            // 172.16.0.0/12 (RFC1918)
            if (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            {
                return true;
            }

            // 192.168.0.0/16 (RFC1918)
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            // 192.0.0.0/24
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
            {
                return true;
            }

            // 198.18.0.0/15 (基准测试网络)
            if (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19))
            {
                return true;
            }
        }
        // IPv4 映射的 IPv6 (::ffff:a.b.c.d)
        else if (address.IsIPv4MappedToIPv6)
        {
            var ipv4 = address.MapToIPv4();
            return IsInternalAddress(ipv4);
        }
        // 原生 IPv6
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // ::1 回环
            if (address.Equals(IPAddress.IPv6Loopback))
            {
                return true;
            }

            // :: 未指定
            if (address.Equals(IPAddress.IPv6Any))
            {
                return true;
            }

            // fc00::/7 (唯一本地地址 ULA)
            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return true;
            }

            // fe80::/10 (链路本地)
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
            {
                return true;
            }
        }

        return false;
    }
}
