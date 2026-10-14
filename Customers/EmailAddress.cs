using System.Text.RegularExpressions;

namespace MasterServer.Customers;

/// <summary>Normalizes and validates buyer emails. Identity is lowercase trimmed email.</summary>
public static class EmailAddress
{
    private static readonly Regex Pattern = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryNormalize(string? input, out string email)
    {
        email = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        var trimmed = input.Trim().ToLowerInvariant();
        if (trimmed.Length > 256) return false;
        if (!Pattern.IsMatch(trimmed)) return false;
        email = trimmed;
        return true;
    }
}
