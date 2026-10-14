using System.Security.Cryptography;
using System.Text;

namespace MasterServer.Security;

/// <summary>Issues opaque bearer tokens and stores only their SHA-256 hash.</summary>
public static class SessionToken
{
    public static string New() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
