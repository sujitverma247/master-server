using System.Text.Json;
using MasterServer.Data;
using MasterServer.Support;

namespace MasterServer.Customers;

public static class CheckoutCapture
{
    public static async Task<CheckoutSession> CaptureAsync(
        HttpContext ctx,
        JsonElement body,
        int customerId,
        string productSlug,
        string planCode,
        string source,
        CancellationToken ct)
    {
        var headerLanguage = ctx.Request.Headers.AcceptLanguage.ToString();
        if (headerLanguage.Contains(',')) headerLanguage = headerLanguage.Split(',')[0];
        if (headerLanguage.Contains(';')) headerLanguage = headerLanguage.Split(';')[0];

        var userAgent = TicketReporter.Clip(FirstNonEmpty(Str(body, "userAgent"), ctx.Request.Headers.UserAgent.ToString()), 256);
        var hint = TicketReporter.FromUserAgent(userAgent);
        var ip = TicketReporter.ClientIp(ctx, FirstNonEmpty(Str(body, "ip"), Str(body, "ipAddress")));
        var language = TicketReporter.Clip(FirstNonEmpty(Str(body, "language"), Str(body, "locale"), Str(body, "lang"), headerLanguage.Trim()), 32);
        var timezone = TicketReporter.Clip(FirstNonEmpty(Str(body, "timezone"), Str(body, "timeZone")), 64);
        var screen = TicketReporter.Clip(FirstNonEmpty(Str(body, "screen"), Str(body, "screenSize")), 32);
        var geo = TicketReporter.Clip(Str(body, "geo"), 200);
        var isp = TicketReporter.Clip(Str(body, "isp"), 200);

        if ((geo.Length == 0 || isp.Length == 0 || timezone.Length == 0) && CheckoutIpLookup.IsPublicIp(ip))
        {
            var lookup = await CheckoutIpLookup.LookupAsync(ip, ct);
            if (geo.Length == 0) geo = lookup.Geo;
            if (isp.Length == 0) isp = lookup.Isp;
            if (timezone.Length == 0) timezone = lookup.Timezone;
        }

        return new CheckoutSession
        {
            CustomerId = customerId,
            ProductSlug = TicketReporter.Clip(productSlug, 64),
            PlanCode = TicketReporter.Clip(planCode, 32),
            Source = TicketReporter.Clip(FirstNonEmpty(source, "website"), 32),
            IpAddress = ip,
            Device = TicketReporter.Clip(FirstNonEmpty(Str(body, "device"), hint.Device), 32),
            OsVersion = TicketReporter.Clip(FirstNonEmpty(Str(body, "os"), Str(body, "osVersion"), hint.Os), 128),
            Browser = TicketReporter.Clip(FirstNonEmpty(Str(body, "browser"), hint.Browser), 64),
            Language = language,
            Timezone = timezone,
            Screen = screen,
            Geo = geo,
            Isp = isp,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static string Str(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "").Trim() : "";

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
            if (value.Length > 0) return value;
        return "";
    }
}
