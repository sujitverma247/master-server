using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MasterServer.Payments;

/// <summary>
/// Local/test gateway. Checkout is completed by posting a signed webhook.
/// Secret comes from GATEWAY_DEV_SECRET (deployment env), never from source.
/// </summary>
public sealed class DevGateway : IPaymentGateway
{
    public const string GatewayName = "dev";
    private const int SkewSeconds = 300;

    private readonly string _secret;

    public DevGateway(string secret) => _secret = secret ?? "";

    public string Name => GatewayName;
    public bool RequiresExternalPlan => false;

    public CheckoutStart Start(OrderStartRequest request) =>
        new($"/v1/checkout/{request.PublicId}", null);

    public bool TryVerify(HttpRequest request, string rawBody, out WebhookEnvelope envelope)
    {
        envelope = new WebhookEnvelope("", "", "", "", null);
        if (string.IsNullOrEmpty(_secret)) return false;

        var eventId = request.Headers["X-Webhook-Id"].ToString();
        var timestamp = request.Headers["X-Webhook-Timestamp"].ToString();
        var nonce = request.Headers["X-Webhook-Nonce"].ToString();
        var signature = request.Headers["X-Webhook-Signature"].ToString();
        if (eventId.Length == 0 || timestamp.Length == 0 || nonce.Length == 0 || signature.Length == 0)
            return false;
        if (nonce.Length > 128 || eventId.Length > 128) return false;
        if (!long.TryParse(timestamp, out var ts)) return false;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - ts) > SkewSeconds) return false;

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrEmpty(rawBody) ? "{}" : rawBody);
            root = doc.RootElement.Clone();
        }
        catch (JsonException) { return false; }

        var eventType = Str(root, "eventType");
        var publicId = Str(root, "publicId");
        var status = Str(root, "status");
        var externalPaymentId = Str(root, "externalPaymentId");
        if (eventType.Length == 0 || publicId.Length == 0 || status.Length == 0) return false;

        if (!SignatureMatches(_secret, eventId, timestamp, nonce, eventType, publicId, status, signature))
            return false;

        envelope = new WebhookEnvelope(eventId, eventType, publicId, status,
            externalPaymentId.Length > 0 ? externalPaymentId : null);
        return true;
    }

    public static string Sign(string secret, string eventId, string timestamp, string nonce,
        string eventType, string publicId, string status)
    {
        var message = $"{eventId}:{timestamp}:{nonce}:{eventType}:{publicId}:{status}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
    }

    public static bool SignatureMatches(string secret, string eventId, string timestamp, string nonce,
        string eventType, string publicId, string status, string signature)
    {
        var expected = Sign(secret, eventId, timestamp, nonce, eventType, publicId, status);
        var a = Encoding.UTF8.GetBytes(signature);
        var b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static string Str(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "").Trim() : "";
}
