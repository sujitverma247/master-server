using System.Security.Cryptography;
using System.Text;
using MasterServer.Data;

namespace MasterServer.Payments;

/// <summary>
/// Freemius buy-link / hosted checkout. Price lives on Freemius; we map plan_id via GatewayPlan.
/// Signature is HMAC-SHA256 of the raw body (hex in X-Signature), product secret from env.
/// </summary>
public sealed class FreemiusGateway : IPaymentGateway
{
    public const string GatewayName = "freemius";

    private readonly string _secret;

    public FreemiusGateway(string secret) => _secret = secret ?? "";

    public string Name => GatewayName;
    public bool RequiresExternalPlan => true;

    public CheckoutStart Start(OrderStartRequest request) => new(null, null);

    public bool TryVerify(HttpRequest request, string rawBody, out WebhookEnvelope envelope)
    {
        envelope = new WebhookEnvelope("", "", "", "", null);
        if (string.IsNullOrEmpty(_secret)) return false;
        if (!SignatureMatches(request, rawBody ?? "", _secret)) return false;
        if (!WebhookJson.TryParse(rawBody, out var root)) return false;

        var eventType = WebhookJson.StrAny(root, "type", "event", "event_type", "name");
        if (eventType.Length == 0) return false;

        var license = WebhookJson.Nested(root, "license");
        var user = WebhookJson.Nested(root, "user");
        var payment = WebhookJson.Nested(root, "payment");
        var subscription = WebhookJson.Nested(root, "subscription");
        var data = WebhookJson.Obj(root, "data");

        var eventId = WebhookJson.FirstNonEmpty(
            WebhookJson.StrAny(root, "id", "event_id", "webhook_id"),
            request.Headers["webhook-id"].ToString(),
            WebhookJson.Sha256Hex(rawBody ?? ""));
        var publicId = WebhookJson.FirstNonEmpty(
            WebhookJson.StrAny(root, "publicId", "public_id"),
            WebhookJson.StrAny(WebhookJson.Obj(root, "metadata"), "publicId", "public_id"));
        var paymentId = WebhookJson.FirstNonEmpty(
            WebhookJson.Str(payment, "id"),
            WebhookJson.StrAny(data, "payment_id"),
            WebhookJson.Str(license, "payment_id"),
            WebhookJson.Str(root, "payment_id"));
        var planId = WebhookJson.FirstNonEmpty(
            WebhookJson.Str(license, "plan_id"),
            WebhookJson.Str(payment, "plan_id"),
            WebhookJson.Str(subscription, "plan_id"),
            WebhookJson.StrAny(data, "plan_id"),
            WebhookJson.Str(root, "plan_id"));
        var email = WebhookJson.FirstNonEmpty(
            WebhookJson.Str(user, "email"),
            WebhookJson.Str(payment, "email"),
            WebhookJson.StrAny(data, "email"),
            WebhookJson.Str(root, "email"));
        var first = WebhookJson.StrAny(user, "first", "first_name");
        var last = WebhookJson.StrAny(user, "last", "last_name");
        var name = WebhookJson.FirstNonEmpty((first + " " + last).Trim(), WebhookJson.Str(user, "name"));
        var userId = WebhookJson.FirstNonEmpty(
            WebhookJson.Str(user, "id"),
            WebhookJson.Str(license, "user_id"),
            WebhookJson.StrAny(data, "user_id"));
        var subscriptionId = WebhookJson.FirstNonEmpty(
            WebhookJson.Str(subscription, "id"),
            WebhookJson.StrAny(data, "subscription_id"),
            WebhookJson.Str(payment, "subscription_id"));
        var locale = WebhookJson.FirstNonEmpty(
            WebhookJson.StrAny(user, "locale", "language", "lang"),
            WebhookJson.StrAny(root, "locale", "language", "lang"));
        var currency = WebhookJson.FirstNonEmpty(
            WebhookJson.Str(payment, "currency"),
            WebhookJson.Str(root, "currency")).ToUpperInvariant();
        var gross = WebhookJson.Dec(payment, "gross") ?? WebhookJson.Dec(root, "gross");
        var vat = WebhookJson.Dec(payment, "vat") ?? WebhookJson.Dec(root, "vat") ?? 0m;
        var amount = gross is null ? null : gross + vat;
        var paidAt = WebhookJson.Dt(payment, "created", "created_at", "paid_at", "date")
            ?? WebhookJson.Dt(license, "created", "created_at")
            ?? WebhookJson.Dt(root, "created", "created_at");
        var expiresAt = WebhookJson.Dt(license, "expiration", "expires_at", "expiry");

        var status = MapStatus(eventType);
        if (status == OrderStatuses.Paid && amount is null && publicId.Length == 0 && paymentId.Length == 0)
            status = "ignored";

        envelope = new WebhookEnvelope(
            eventId,
            eventType,
            publicId,
            status,
            paymentId.Length > 0 ? paymentId : null,
            ExternalPlanId: planId.Length > 0 ? planId : null,
            Email: email.Length > 0 ? email : null,
            Amount: amount,
            Currency: currency.Length > 0 ? currency : null,
            CustomerName: name.Length > 0 ? name : null,
            ExternalCustomerId: userId.Length > 0 ? userId : null,
            ExternalSubscriptionId: subscriptionId.Length > 0 ? subscriptionId : null,
            PaidAt: paidAt,
            ExpiresAt: expiresAt,
            IsSubscription: subscriptionId.Length > 0,
            Locale: locale.Length > 0 ? locale : null);
        return true;
    }

    public static string SignHex(string secret, string rawBody)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? ""));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody ?? ""))).ToLowerInvariant();
    }

    public static bool SignatureMatches(HttpRequest request, string rawBody, string secret)
    {
        if (string.IsNullOrEmpty(secret)) return false;

        var querySecret = WebhookJson.FirstNonEmpty(request.Query["secret"].ToString(), request.Query["token"].ToString());
        var auth = request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            auth = auth["Bearer ".Length..].Trim();
        var headerSecret = request.Headers["X-Webhook-Secret"].ToString();
        if (WebhookJson.FixedEquals(querySecret, secret)
            || WebhookJson.FixedEquals(auth, secret)
            || WebhookJson.FixedEquals(headerSecret, secret))
            return true;

        var expected = SignHex(secret, rawBody);
        foreach (var headerName in new[] { "X-Signature", "x-signature", "X-Freemius-Signature", "X-Fs-Signature" })
        {
            var signature = request.Headers[headerName].ToString().Trim();
            if (signature.Length == 0) continue;
            if (signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
                signature = signature[7..];
            if (WebhookJson.FixedEquals(signature.ToLowerInvariant(), expected))
                return true;
        }
        return false;
    }

    internal static string MapStatus(string eventType) => eventType.Trim().ToLowerInvariant() switch
    {
        "payment.created" or "license.created" or "license.activated" or "subscription.created"
            or "license.extended" or "license.updated" or "license.plan.changed" => OrderStatuses.Paid,
        "payment.refund" or "payment.refunded" or "payment.dispute.lost"
            or "license.deleted" or "license.cancelled" => OrderStatuses.Refunded,
        "subscription.cancelled" => OrderStatuses.Cancelled,
        "subscription.renewal.failed" or "subscription.renewal.failed.last" or "license.expired" => OrderStatuses.Failed,
        _ => "ignored"
    };
}
