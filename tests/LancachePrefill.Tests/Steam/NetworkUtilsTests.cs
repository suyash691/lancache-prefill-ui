using System.Net;
using LancachePrefill;
using Xunit;

namespace LancachePrefill.Tests;

public class NetworkUtilsTests
{
    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("10.255.255.255", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("192.168.50.110", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("1.1.1.1", false)]
    [InlineData("172.15.0.1", false)]   // just below 172.16
    [InlineData("172.32.0.1", false)]   // just above 172.31
    [InlineData("192.169.1.1", false)]  // not 192.168
    [InlineData("11.0.0.1", false)]
    public void IsPrivateIp(string ip, bool expected)
    {
        Assert.Equal(expected, NetworkUtils.IsPrivateIp(IPAddress.Parse(ip)));
    }

    [Fact]
    public void IsPrivateIp_IPv6_ReturnsFalse()
    {
        Assert.False(NetworkUtils.IsPrivateIp(IPAddress.IPv6Loopback));
    }
}
