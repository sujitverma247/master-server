namespace MasterServer.Mail;

/// <summary>
/// Maps a checkout/OS/browser language to the two mail catalogs, the same way the
/// desktop app maps Windows display language: German family → de, everything else → en.
/// </summary>
public static class MailLocale
{
    public const string English = "en";
    public const string German = "de";

    public static string Normalize(string? raw)
    {
        var value = (raw ?? "").Trim().ToLowerInvariant().Replace('_', '-');
        if (value.Length == 0) return English;

        var primary = value.Split(',')[0].Trim().Split(';')[0].Trim();
        if (primary.Length == 0) return English;
        if (primary == German || primary.StartsWith("de-", StringComparison.Ordinal))
            return German;
        return English;
    }

    public static bool IsGerman(string? raw) => Normalize(raw) == German;
}
