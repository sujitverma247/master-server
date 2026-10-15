namespace MasterServer.Catalog;

/// <summary>Fetches a website logo for PDFs. Size-capped; failures return null so the seller name is used.</summary>
public static class BrandLogo
{
    private static readonly HttpClient Http = CreateClient();
    private const int MaxLogoBytes = 512 * 1024;

    public static async Task<byte[]?> TryFetchAsync(string? logoUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(logoUrl)) return null;
        if (!Uri.TryCreate(logoUrl.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode) return null;
            var type = res.Content.Headers.ContentType?.MediaType ?? "";
            if (type.Length > 0 && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
            var len = res.Content.Headers.ContentLength;
            if (len > MaxLogoBytes) return null;
            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var ms = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                if (ms.Length + read > MaxLogoBytes) return null;
                await ms.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            return ms.Length == 0 ? null : ms.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MasterServer-Logo/1.0");
        return http;
    }
}
