using Microsoft.AspNetCore.Http;

namespace MasterServer.Support;

public static class TicketReporter
{
    public static string Clip(string? value, int max)
    {
        var s = (value ?? "").Trim();
        if (s.Length > max) s = s[..max];
        return s;
    }

    public static string DeviceLabel(string device, string os, string browser)
    {
        return string.Join(" · ", new[] { device, os, browser }.Where(part => part.Length > 0));
    }

    public static string ComposeGeo(string geo, string city, string region, string country)
    {
        if (geo.Length > 0) return geo;
        return string.Join(", ", new[] { city, region, country }.Where(part => part.Length > 0));
    }

    public static string ComposeAddress(string full, string street, string city, string region, string postal, string country)
    {
        var fromParts = string.Join(", ", new[] { street, city, region, postal, country }.Where(part => part.Length > 0));
        if (fromParts.Length > 0) return fromParts;
        return full.Trim();
    }

    public static string ClientIp(HttpContext ctx, string? reported)
    {
        var fromBody = Clip(reported, 64);
        if (fromBody.Length > 0) return fromBody;
        var forwarded = ctx.Request.Headers["X-Forwarded-For"].ToString();
        if (forwarded.Length > 0)
        {
            var first = forwarded.Split(',')[0].Trim();
            if (first.Length > 0) return Clip(first, 64);
        }
        return Clip(ctx.Connection.RemoteIpAddress?.ToString(), 64);
    }

    public static (string Device, string Os, string Browser) FromUserAgent(string? userAgent)
    {
        var ua = userAgent ?? "";
        if (ua.Length == 0) return ("", "", "");
        var browser =
            ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge" :
            ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox" :
            ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome" :
            ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari" : "";
        var os =
            ua.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android" :
            ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iOS" :
            ua.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase) || ua.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) ? "macOS" :
            ua.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows" :
            ua.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux" : "";
        var device =
            ua.Contains("Mobi", StringComparison.OrdinalIgnoreCase) || os is "Android" or "iOS" ? "Mobile" : "Desktop";
        return (device, os, browser);
    }
}
