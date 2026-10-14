using System.Text.RegularExpressions;

namespace MasterServer.Customers;

/// <summary>Policy version codes: 2-64 chars of a-z, 0-9, dot, hyphen.</summary>
public static class PolicyVersion
{
    private static readonly Regex Pattern = new(
        @"^[a-z0-9][a-z0-9.\-]{1,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryNormalize(string? input, out string version)
    {
        version = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        var trimmed = input.Trim().ToLowerInvariant();
        if (!Pattern.IsMatch(trimmed)) return false;
        version = trimmed;
        return true;
    }
}
