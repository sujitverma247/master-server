using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MasterServer.Payments;

/// <summary>
/// Local/test gateway. Checkout is optional; buy-link style payloads omit publicId
/// and send email + externalPlanId + amount. Secret comes from GATEWAY_DEV_SECRET.
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
        if (eventType.Length == 0 || status.Length == 0) return false;

        if (!SignatureMatches(_secret, eventId, timestamp, nonce, eventType, publicId, status, signature))
            return false;

        var externalPaymentId = StrAny(root, "externalPaymentId", "paymentId");
        var externalPlanId = StrAny(root, "externalPlanId", "gatewayPlanId", "productId");
        var email = StrAny(root, "email", "customerEmail");
        var currency = Str(root, "currency");
        var name = StrAny(root, "customerName", "name");
        var externalCustomerId = Str(root, "externalCustomerId");
        var externalSubscriptionId = StrAny(root, "externalSubscriptionId", "subscriptionId");

        envelope = new WebhookEnvelope(
            eventId,
            eventType,
            publicId,
            status,
            externalPaymentId.Length > 0 ? externalPaymentId : null,
            ExternalPlanId: externalPlanId.Length > 0 ? externalPlanId : null,
            Email: email.Length > 0 ? email : null,
            Amount: Dec(root, "amount"),
            Currency: currency.Length > 0 ? currency : null,
            CustomerName: name.Length > 0 ? name : null,
            ExternalCustomerId: externalCustomerId.Length > 0 ? externalCustomerId : null,
            ExternalSubscriptionId: externalSubscriptionId.Length > 0 ? externalSubscriptionId : null,
            PaidAt: Dt(root, "paidAt"),
            ExpiresAt: Dt(root, "expiresAt"),
            IsSubscription: Flag(root, "isSubscription"));
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

    private static string StrAny(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            var value = Str(root, name);
            if (value.Length > 0) return value;
        }
        return "";
    }

    private static string Str(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "").Trim() : "";

    private static decimal? Dec(JsonElement body, string name)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number))
            return number;
        return null;
    }

    private static DateTime? Dt(JsonElement body, string name)
    {
        var text = Str(body, name);
        if (text.Length == 0) return null;
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return null;
        if (dt.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        return dt.ToUniversalTime();
    }

    private static bool Flag(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
