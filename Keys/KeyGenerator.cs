using System.Security.Cryptography;

namespace MasterServer.Keys;

/// <summary>
/// Generates license keys in the form PREFIX-XXXXX-XXXXX-XXXXX-XXXXX using an unambiguous
/// alphabet (no 0/O/1/I). The prefix comes from the product; existing keys are never rewritten.
/// </summary>
public static class KeyGenerator
{
    // 31 chars: A-Z and 2-9, with I, O, 0, 1 removed to avoid visual confusion.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Groups = 4;
    private const int GroupLength = 5;

    public static string Generate(string prefix)
    {
        var clean = Normalize(prefix);
        var parts = new string[Groups];
        for (var g = 0; g < Groups; g++)
        {
            var chars = new char[GroupLength];
            for (var i = 0; i < GroupLength; i++)
                chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            parts[g] = new string(chars);
        }
        var body = string.Join('-', parts);
        return clean.Length > 0 ? $"{clean}-{body}" : body;
    }

    /// <summary>Uppercases and keeps only alphabet chars; clamps to 4 characters.</summary>
    public static string Normalize(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return "";
        var filtered = new string(prefix.Trim().ToUpperInvariant()
            .Where(c => Alphabet.Contains(c)).ToArray());
        return filtered.Length > 4 ? filtered[..4] : filtered;
    }

    public static bool IsValidFormat(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var parts = key.Split('-');
        if (parts.Length < Groups) return false;
        // The last `Groups` segments must each be GroupLength chars from the alphabet.
        var body = parts[^Groups..];
        return body.All(p => p.Length == GroupLength && p.All(Alphabet.Contains));
    }
}
