using System.Net;
using System.Text.Json;
using MasterServer.Support;

namespace MasterServer.Customers;

/// <summary>Fills city/region/country and ISP from a public IP. Failures stay empty so checkout is not blocked.</summary>
public static class CheckoutIpLookup
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public readonly record struct Result(string Geo, string Isp, string Timezone);

    public static bool IsPublicIp(string ip)
    {
        if (!IPAddress.TryParse(ip, out var parsed)) return false;
        if (IPAddress.IsLoopback(parsed)) return false;
        var bytes = parsed.GetAddressBytes();
        if (parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            if (bytes[0] == 10) return false;
            if (bytes[0] == 127) return false;
            if (bytes[0] == 169 && bytes[1] == 254) return false;
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;
            if (bytes[0] == 192 && bytes[1] == 168) return false;
            return true;
        }

        if (parsed.IsIPv4MappedToIPv6) return IsPublicIp(parsed.MapToIPv4().ToString());
        return !parsed.IsIPv6LinkLocal && !parsed.IsIPv6SiteLocal && !parsed.IsIPv6Multicast;
    }

    public static async Task<Result> LookupAsync(string ip, CancellationToken ct)
    {
        if (!IsPublicIp(ip)) return default;
        try
        {
            using var res = await Http.GetAsync($"https://ipwho.is/{Uri.EscapeDataString(ip)}?fields=success,country,region,city,isp,connection,timezone", ct);
            if (!res.IsSuccessStatusCode) return default;
            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return default;
            if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False) return default;
            var city = Str(root, "city");
            var region = Str(root, "region");
            var country = Str(root, "country");
            var geo = TicketReporter.Clip(TicketReporter.ComposeGeo("", city, region, country), 200);
            var isp = TicketReporter.Clip(FirstNonEmpty(Str(root, "isp"), Nested(root, "connection", "isp"), Nested(root, "connection", "org")), 200);
            var timezone = TicketReporter.Clip(FirstNonEmpty(Str(root, "timezone"), Nested(root, "timezone", "id")), 64);
            return new Result(geo, isp, timezone);
        }
        catch
        {
            return default;
        }
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "EuropeDigiCheckout/1.0");
        return http;
    }

    private static string Str(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "").Trim() : "";

    private static string Nested(JsonElement obj, string parent, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(parent, out var child)
            ? Str(child, name) : "";

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
            if (value.Length > 0) return value;
        return "";
    }
}
