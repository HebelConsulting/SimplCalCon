using System.Net;
using SimplCalCon.Infrastructure.Storage;

namespace SimplCalCon.UnitTests;

public class SsrfSafeConnectTests
{
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")] // example.com
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")]
    public void Allows_public_addresses(string ip) =>
        Assert.True(SsrfSafeConnect.IsPublic(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("127.0.0.1")]      // loopback
    [InlineData("10.1.2.3")]       // private /8
    [InlineData("172.16.5.4")]     // private /12
    [InlineData("172.31.255.1")]   // private /12 (upper bound)
    [InlineData("192.168.1.1")]    // private /16
    [InlineData("169.254.10.10")]  // link-local
    [InlineData("100.64.0.1")]     // carrier-grade NAT
    [InlineData("0.0.0.0")]        // "this network"
    [InlineData("224.0.0.1")]      // multicast
    [InlineData("::1")]            // IPv6 loopback
    [InlineData("fe80::1")]        // IPv6 link-local
    [InlineData("fc00::1")]        // IPv6 unique-local
    [InlineData("fd12:3456::1")]   // IPv6 unique-local
    [InlineData("::ffff:127.0.0.1")] // IPv4-mapped loopback
    [InlineData("::ffff:10.0.0.1")]  // IPv4-mapped private
    public void Blocks_non_public_addresses(string ip) =>
        Assert.False(SsrfSafeConnect.IsPublic(IPAddress.Parse(ip)));

    [Fact]
    public void Public_172_15_and_172_32_are_not_private()
    {
        // 172.16.0.0/12 is private, but 172.15.x and 172.32.x are outside it.
        Assert.True(SsrfSafeConnect.IsPublic(IPAddress.Parse("172.15.0.1")));
        Assert.True(SsrfSafeConnect.IsPublic(IPAddress.Parse("172.32.0.1")));
    }
}
