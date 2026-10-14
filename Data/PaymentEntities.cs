namespace MasterServer.Data;

/// <summary>A checkout/order. Amount is copied from the Plan row; the client cannot set the price.</summary>
public class Order
{
    public int Id { get; set; }
    public string PublicId { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int ProductId { get; set; }
    public string PlanType { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Gateway { get; set; } = "";
    public string Status { get; set; } = OrderStatuses.Pending;
    public int? LicenseId { get; set; }
    public License? License { get; set; }
    public int? RefundPolicyId { get; set; }
    public string? ExternalCheckoutId { get; set; }
    public string? ExternalPaymentId { get; set; }
    public string? ExternalSubscriptionId { get; set; }
    public string? CheckoutUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
}

public static class OrderStatuses
{
    public const string Pending = "pending";
    public const string Paid = "paid";
    public const string Failed = "failed";
    public const string Refunded = "refunded";
    public const string Cancelled = "cancelled";
}

/// <summary>Idempotency row for a gateway webhook. Unique per (gateway, externalEventId).</summary>
public class PaymentEvent
{
    public long Id { get; set; }
    public string Gateway { get; set; } = "";
    public string ExternalEventId { get; set; } = "";
    public string EventType { get; set; } = "";
    public int? OrderId { get; set; }
    public string PayloadSha256 { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Maps our (product, plan code) to a gateway's catalog id. External ids are deployment rows,
/// never compiled into source.
/// </summary>
public class GatewayPlan
{
    public int Id { get; set; }
    public string Gateway { get; set; } = "";
    public int ProductId { get; set; }
    public string PlanCode { get; set; } = "";
    public string ExternalPlanId { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
