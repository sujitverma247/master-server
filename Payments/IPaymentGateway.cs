namespace MasterServer.Payments;

public sealed record CheckoutStart(string? CheckoutUrl, string? ExternalCheckoutId);

public sealed record WebhookEnvelope(
    string EventId,
    string EventType,
    string PublicId,
    string Status,
    string? ExternalPaymentId);

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
