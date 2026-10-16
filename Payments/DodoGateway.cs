using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasterServer.Data;

namespace MasterServer.Payments;

/// <summary>
/// Dodo Payments buy-link / hosted checkout. Price lives on Dodo; we map product_id via GatewayPlan.
/// Signatures follow Standard Webhooks (webhook-id / timestamp / signature).
/// </summary>
public sealed class DodoGateway : IPaymentGateway
{
    public const string GatewayName = "dodo";
    private const int SkewSeconds = 300;

    private readonly string _secret;

    public DodoGateway(string secret) => _secret = secret ?? "";

    public string Name => GatewayName;
    public bool RequiresExternalPlan => true;

    public CheckoutStart Start(OrderStartRequest request) => new(null, null);

    public bool TryVerify(HttpRequest request, string rawBody, out WebhookEnvelope envelope)
    {
        envelope = new WebhookEnvelope("", "", "", "", null);
        if (string.IsNullOrEmpty(_secret)) return false;

        var webhookId = request.Headers["webhook-id"].ToString();
        var timestamp = request.Headers["webhook-timestamp"].ToString();
        var signature = request.Headers["webhook-signature"].ToString();
        if (!SignatureMatches(_secret, webhookId, timestamp, rawBody ?? "", signature))
            return false;
        if (!WebhookJson.TryParse(rawBody, out var root)) return false;

        var eventType = WebhookJson.Str(root, "type");
        if (eventType.Length == 0) return false;

        var data = WebhookJson.Obj(root, "data");
        if (data.ValueKind != JsonValueKind.Object) data = root;
        var customer = WebhookJson.Obj(data, "customer");
        var metadata = WebhookJson.Obj(data, "metadata");
        var cartItem = WebhookJson.FirstArrayItem(data, "product_cart");

        var publicId = WebhookJson.FirstNonEmpty(
            WebhookJson.StrAny(metadata, "publicId", "public_id"),
            WebhookJson.StrAny(root, "publicId", "public_id"));
        var paymentId = WebhookJson.FirstNonEmpty(
            WebhookJson.StrAny(data, "payment_id", "paymentId"),
            WebhookJson.Str(root, "payment_id"));
        var productId = WebhookJson.FirstNonEmpty(
            WebhookJson.Str(cartItem, "product_id"),
            WebhookJson.StrAny(data, "product_id", "productId"),
            WebhookJson.StrAny(metadata, "externalPlanId", "planId", "productId"));
        var email = WebhookJson.FirstNonEmpty(
            WebhookJson.Str(customer, "email"),
            WebhookJson.Str(data, "email"),
            WebhookJson.Str(metadata, "email"));
        var name = WebhookJson.FirstNonEmpty(
            WebhookJson.Str(customer, "name"),
            WebhookJson.StrAny(data, "customer_name", "name"));
        var customerId = WebhookJson.FirstNonEmpty(
            WebhookJson.StrAny(customer, "customer_id", "id"),
            WebhookJson.Str(data, "customer_id"));
        var subscriptionId = WebhookJson.StrAny(data, "subscription_id", "subscriptionId");
        var currency = WebhookJson.FirstNonEmpty(
            WebhookJson.Str(data, "currency"),
            WebhookJson.Str(root, "currency")).ToUpperInvariant();
        var amount = FromMinorUnits(WebhookJson.Dec(data, "total_amount", "settlement_amount", "amount"), currency);
        var paidAt = WebhookJson.Dt(data, "created_at", "paid_at")
            ?? WebhookJson.Dt(root, "timestamp");
        var locale = WebhookJson.StrAny(metadata, "locale", "lang", "language");
        var eventId = webhookId.Length > 0 ? webhookId : WebhookJson.Sha256Hex(rawBody ?? "");

        var status = MapStatus(eventType);
        if (status == OrderStatuses.Paid && amount is null && publicId.Length == 0 && paymentId.Length == 0)
            status = "ignored";

        envelope = new WebhookEnvelope(
            eventId,
            eventType,
            publicId,
            status,
            paymentId.Length > 0 ? paymentId : null,
            ExternalPlanId: productId.Length > 0 ? productId : null,
            Email: email.Length > 0 ? email : null,
            Amount: amount,
            Currency: currency.Length > 0 ? currency : null,
            CustomerName: name.Length > 0 ? name : null,
            ExternalCustomerId: customerId.Length > 0 ? customerId : null,
            ExternalSubscriptionId: subscriptionId.Length > 0 ? subscriptionId : null,
            PaidAt: paidAt,
            IsSubscription: subscriptionId.Length > 0 || eventType.StartsWith("subscription.", StringComparison.OrdinalIgnoreCase),
            Locale: locale.Length > 0 ? locale : null);
        return true;
    }

    public static string Sign(string secret, string webhookId, string timestamp, string rawBody)
    {
        var signed = $"{webhookId}.{timestamp}.{rawBody}";
        using var hmac = new HMACSHA256(DecodeSecret(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signed)));
    }

    public static bool SignatureMatches(string secret, string webhookId, string timestamp, string rawBody, string signatureHeader)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(timestamp))
            return false;
        if (!long.TryParse(timestamp, out var ts)) return false;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - ts) > SkewSeconds) return false;

        var expected = Sign(secret, webhookId, timestamp, rawBody);
        foreach (var part in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var sig = part.Contains(',') ? part[(part.IndexOf(',') + 1)..] : part;
            if (WebhookJson.FixedEquals(sig, expected)) return true;
        }
        return false;
    }

    public static decimal? FromMinorUnits(decimal? raw, string currency)
    {
        if (raw is null) return null;
        if (raw.Value != decimal.Truncate(raw.Value))
            return Money.Normalize(raw.Value);
        var code = (currency ?? "").Trim().ToUpperInvariant();
        if (code is "JPY" or "KRW" or "VND" or "CLP")
            return Money.Normalize(raw.Value);
        return Money.Normalize(raw.Value / 100m);
    }

    internal static string MapStatus(string eventType) => eventType.Trim().ToLowerInvariant() switch
    {
        "payment.succeeded" or "subscription.active" or "subscription.renewed" or "license_key.created" => OrderStatuses.Paid,
        "refund.succeeded" => OrderStatuses.Refunded,
        "payment.failed" or "subscription.failed" => OrderStatuses.Failed,
        "payment.cancelled" or "subscription.cancelled" => OrderStatuses.Cancelled,
        _ => "ignored"
    };

    internal static byte[] DecodeSecret(string secret)
    {
        var s = secret.StartsWith("whsec_", StringComparison.Ordinal) ? secret["whsec_".Length..] : secret;
        try { return Convert.FromBase64String(s); }
        catch { return Encoding.UTF8.GetBytes(secret); }
    }
}
