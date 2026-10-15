namespace MasterServer.Payments;

public sealed record CheckoutStart(string? CheckoutUrl, string? ExternalCheckoutId);

/// <summary>
/// Normalized gateway webhook. PublicId is set when the site started checkout first;
/// buy-link payments omit it and identify the product with ExternalPlanId + Email.
/// Amount/currency are the gateway charge, not our Plan.Price snapshot.
/// </summary>
public sealed record WebhookEnvelope(
    string EventId,
    string EventType,
    string PublicId,
    string Status,
    string? ExternalPaymentId,
    string? ExternalPlanId = null,
    string? Email = null,
    decimal? Amount = null,
    string? Currency = null,
    string? CustomerName = null,
    string? ExternalCustomerId = null,
    string? ExternalSubscriptionId = null,
    DateTime? PaidAt = null,
    DateTime? ExpiresAt = null,
    bool IsSubscription = false,
    string? Locale = null);

/// <summary>
/// One payment provider. Name is the URL/path segment and Order.Gateway value.
/// External catalog ids live in GatewayPlan rows, not in the adapter.
/// </summary>
public interface IPaymentGateway
{
    string Name { get; }
    bool RequiresExternalPlan { get; }
    CheckoutStart Start(OrderStartRequest request);
    bool TryVerify(HttpRequest request, string rawBody, out WebhookEnvelope envelope);
}

public sealed record OrderStartRequest(
    string PublicId,
    string Email,
    string PlanType,
    decimal Amount,
    string Currency,
    string? ExternalPlanId);
