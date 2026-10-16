using MasterServer.Admin;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace MasterServer.Tests;

public class AuditTests
{
    [Fact]
    public void Client_ip_prefers_forwarded_then_remote()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Forwarded-For"] = "10.1.1.1, 10.2.2.2";
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");
        Assert.Equal("10.1.1.1", Audit.ClientIp(ctx));
        ctx.Request.Headers.Remove("X-Forwarded-For");
        Assert.Equal("127.0.0.1", Audit.ClientIp(ctx));
    }
}
