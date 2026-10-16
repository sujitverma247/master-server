using MasterServer.Support;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace MasterServer.Tests;

public class TicketReporterTests
{
    [Fact]
    public void Clip_trims_and_caps()
    {
        Assert.Equal("", TicketReporter.Clip("  ", 8));
        Assert.Equal("abcd", TicketReporter.Clip("  abcd  ", 8));
        Assert.Equal("abcdef", TicketReporter.Clip("abcdefghij", 6));
    }

    [Fact]
    public void Device_label_joins_non_empty_parts()
    {
        Assert.Equal("Desktop · macOS · Firefox", TicketReporter.DeviceLabel("Desktop", "macOS", "Firefox"));
        Assert.Equal("Firefox", TicketReporter.DeviceLabel("", "", "Firefox"));
        Assert.Equal("", TicketReporter.DeviceLabel("", "", ""));
    }

    [Fact]
    public void Compose_address_joins_website_form_parts()
    {
        Assert.Equal(
            "17 fgghghfh hhghgh, Doonan, Queensland, 4562, Australia",
            TicketReporter.ComposeAddress("", "17 fgghghfh hhghgh", "Doonan", "Queensland", "4562", "Australia"));
        Assert.Equal(
            "17 fgghghfh hhghgh, Doonan, Queensland, 4562, Australia",
            TicketReporter.ComposeAddress("ignored", "17 fgghghfh hhghgh", "Doonan", "Queensland", "4562", "Australia"));
        Assert.Equal("United States of America", TicketReporter.ComposeAddress("", "", "", "", "", "United States of America"));
        Assert.Equal("1 Main St, Austin, TX", TicketReporter.ComposeAddress("1 Main St, Austin, TX", "", "", "", "", ""));
    }

    [Fact]
    public void Compose_geo_prefers_full_string_then_parts()
    {
        Assert.Equal("Brisbane, Queensland, Australia", TicketReporter.ComposeGeo("Brisbane, Queensland, Australia", "Sydney", "NSW", "Australia"));
        Assert.Equal("Brisbane, Queensland, Australia", TicketReporter.ComposeGeo("", "Brisbane", "Queensland", "Australia"));
        Assert.Equal("Australia", TicketReporter.ComposeGeo("", "", "", "Australia"));
        Assert.Equal("", TicketReporter.ComposeGeo("", "", "", ""));
    }

    [Fact]
    public void Client_ip_prefers_reported_then_forwarded_then_remote()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Forwarded-For"] = "10.1.1.1, 10.2.2.2";
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");
        Assert.Equal("111.126.85.455", TicketReporter.ClientIp(ctx, " 111.126.85.455 "));
        Assert.Equal("10.1.1.1", TicketReporter.ClientIp(ctx, " "));
        ctx.Request.Headers.Remove("X-Forwarded-For");
        Assert.Equal("127.0.0.1", TicketReporter.ClientIp(ctx, null));
    }

    [Fact]
    public void User_agent_maps_desktop_firefox_macos()
    {
        var hint = TicketReporter.FromUserAgent(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:128.0) Gecko/20100101 Firefox/128.0");
        Assert.Equal("Desktop", hint.Device);
        Assert.Equal("macOS", hint.Os);
        Assert.Equal("Firefox", hint.Browser);
    }

    [Fact]
    public void User_agent_maps_mobile_chrome_android()
    {
        var hint = TicketReporter.FromUserAgent(
            "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Mobile Safari/537.36");
        Assert.Equal("Mobile", hint.Device);
        Assert.Equal("Android", hint.Os);
        Assert.Equal("Chrome", hint.Browser);
    }

    [Fact]
    public void Empty_user_agent_is_blank()
    {
        var empty = TicketReporter.FromUserAgent("");
        Assert.Equal("", empty.Device);
        Assert.Equal("", empty.Os);
        Assert.Equal("", empty.Browser);
        var missing = TicketReporter.FromUserAgent(null);
        Assert.Equal("", missing.Device);
    }
}
