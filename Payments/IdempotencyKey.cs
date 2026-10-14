using System.Text.RegularExpressions;

namespace MasterServer.Payments;

public static class IdempotencyKey
{
    private static readonly Regex Pattern = new(
        @"^[A-Za-z0-9._\-]{8,128}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string New() => Guid.NewGuid().ToString("N");

    public static bool TryNormalize(string? input, out string key)
    {
        key = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        var trimmed = input.Trim();
        if (!Pattern.IsMatch(trimmed)) return false;
        key = trimmed;
        return true;
    }

    public static string Sha256Hex(string raw)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
