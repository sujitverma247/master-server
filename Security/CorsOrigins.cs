namespace MasterServer.Security;

/// <summary>
/// Browser origins for admin panel and the public marketing site.
/// </summary>
public static class CorsOrigins
{
    public static string[] FromEnvironment(IConfiguration config)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var origins = new List<string>();
        Add(origins, seen, Environment.GetEnvironmentVariable("ADMIN_ORIGIN") ?? config["Admin:Origin"]);
        Add(origins, seen, Environment.GetEnvironmentVariable("WEBSITE_ORIGIN") ?? config["Website:Origin"]);
        Add(origins, seen, "http://localhost:3000");
        Add(origins, seen, "http://127.0.0.1:3000");
        Add(origins, seen, "https://europedigisystems.com");
        return origins.ToArray();
    }

    private static void Add(List<string> origins, HashSet<string> seen, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length == 0 || !seen.Add(part)) continue;
            origins.Add(part);
        }
    }
}
