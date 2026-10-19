using MasterServer.Customers;
using Xunit;

namespace MasterServer.Tests;

public class CheckoutIpLookupTests
{
    [Fact]
    public void Public_ipv4_is_accepted()
    {
        Assert.True(CheckoutIpLookup.IsPublicIp("8.8.8.8"));
        Assert.True(CheckoutIpLookup.IsPublicIp("1.1.1.1"));
    }

    [Fact]
    public void Private_and_loopback_are_skipped()
    {
        Assert.False(CheckoutIpLookup.IsPublicIp(""));
        Assert.False(CheckoutIpLookup.IsPublicIp("not-an-ip"));
        Assert.False(CheckoutIpLookup.IsPublicIp("127.0.0.1"));
        Assert.False(CheckoutIpLookup.IsPublicIp("10.0.0.8"));
        Assert.False(CheckoutIpLookup.IsPublicIp("192.168.1.20"));
        Assert.False(CheckoutIpLookup.IsPublicIp("172.16.0.2"));
        Assert.False(CheckoutIpLookup.IsPublicIp("169.254.1.1"));
        Assert.False(CheckoutIpLookup.IsPublicIp("::1"));
    }
}
