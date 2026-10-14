using System.Security.Cryptography;
using System.Text;

namespace MasterServer.Security;

public static class HmacSignature
{
    public static string Sign(string secret, string appKey, string timestamp, string nonce, string path, string query)
    {
        var message = $"{appKey}:{timestamp}:{nonce}:{path}:{query}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
    }

    public static bool Matches(string secret, string appKey, string timestamp, string nonce, string path, string query, string signature)
    {
        var expected = Sign(secret, appKey, timestamp, nonce, path, query);
        var actualBytes = Encoding.UTF8.GetBytes(signature);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        if (actualBytes.Length != expectedBytes.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
