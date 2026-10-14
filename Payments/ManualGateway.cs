namespace MasterServer.Payments;

/// <summary>Offline / admin-completed payment. No webhook; staff call mark-paid.</summary>
public sealed class ManualGateway : IPaymentGateway
{
    public const string GatewayName = "manual";

    public string Name => GatewayName;
    public bool RequiresExternalPlan => false;

    public CheckoutStart Start(OrderStartRequest request) => new(null, null);

    public bool TryVerify(HttpRequest request, string rawBody, out WebhookEnvelope envelope)
    {
        envelope = new WebhookEnvelope("", "", "", "", null);
        return false;
    }
}
