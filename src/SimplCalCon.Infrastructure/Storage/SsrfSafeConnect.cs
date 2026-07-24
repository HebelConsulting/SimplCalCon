using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>
/// A <see cref="SocketsHttpHandler.ConnectCallback"/> that blocks Server-Side Request Forgery when
/// fetching contact-photo URLs sourced from user-supplied vCards (ADR 0037). Validation happens at
/// connect time on the *resolved* IP, so it also defends against DNS rebinding and redirects that
/// land on a private host — only a genuinely public address is ever connected to.
/// </summary>
public static class SsrfSafeConnect
{
    public static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);
        var target = Array.Find(addresses, IsPublic)
            ?? throw new HttpRequestException($"Host '{endpoint.Host}' does not resolve to a permitted public address.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(target, endpoint.Port), cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip))
        {
            return false;
        }

        return ip.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicV4(ip.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPublicV6(ip),
            _ => false,
        };
    }

    private static bool IsPublicV4(byte[] b) => (b[0], b[1]) switch
    {
        (0, _) => false,                       // 0.0.0.0/8 "this network"
        (10, _) => false,                      // 10.0.0.0/8 private
        (127, _) => false,                     // loopback
        (169, 254) => false,                   // 169.254.0.0/16 link-local
        (172, >= 16 and <= 31) => false,       // 172.16.0.0/12 private
        (192, 168) => false,                   // 192.168.0.0/16 private
        (100, >= 64 and <= 127) => false,      // 100.64.0.0/10 carrier-grade NAT
        (>= 224, _) => false,                  // 224.0.0.0/4 multicast + 240.0.0.0/4 reserved
        _ => true,
    };

    private static bool IsPublicV6(IPAddress ip)
    {
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
        {
            return false;
        }

        var b = ip.GetAddressBytes();
        // fc00::/7 unique local addresses.
        return (b[0] & 0xFE) != 0xFC;
    }
}
