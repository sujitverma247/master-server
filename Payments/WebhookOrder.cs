using MasterServer.Customers;
using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Payments;

/// <summary>
/// Matches a webhook to an existing order, or creates one from a GatewayPlan mapping
/// (website buy-link: no pending checkout row).
/// </summary>
public static class WebhookOrder
{
    public static string? ValidateCreate(WebhookEnvelope envelope)
    {
        if (!EmailAddress.TryNormalize(envelope.Email, out _)) return "invalid email";
        if (string.IsNullOrWhiteSpace(envelope.ExternalPlanId)) return "missing externalPlanId";
        if (envelope.Amount is null) return "missing amount";
        if (!Money.IsNonNegative(envelope.Amount.Value)) return "invalid amount";
        var currency = (envelope.Currency ?? "").Trim();
        if (currency.Length is < 3 or > 8) return "missing currency";
        return null;
    }

    public static async Task<(Order? Order, string? Error)> ResolveOrCreateAsync(
        AppDb db, string gatewayName, WebhookEnvelope envelope, CancellationToken ct)
    {
        if (envelope.PublicId.Length > 0)
        {
            var byPublic = await db.Orders.FirstOrDefaultAsync(o => o.PublicId == envelope.PublicId, ct);
            if (byPublic is not null)
            {
                if (!string.Equals(byPublic.Gateway, gatewayName, StringComparison.OrdinalIgnoreCase))
                    return (null, "gateway mismatch");
                StampCharge(byPublic, envelope);
                return (byPublic, null);
            }
        }

        if (!string.IsNullOrEmpty(envelope.ExternalPaymentId))
        {
            var byPay = await db.Orders.FirstOrDefaultAsync(
                o => o.Gateway == gatewayName && o.ExternalPaymentId == envelope.ExternalPaymentId, ct);
            if (byPay is not null)
            {
                StampCharge(byPay, envelope);
                return (byPay, null);
            }
        }

        var status = envelope.Status.Trim().ToLowerInvariant();
        if (status == OrderStatuses.Refunded)
            return (null, "order not found");

        var error = ValidateCreate(envelope);
        if (error is not null) return (null, error);

        EmailAddress.TryNormalize(envelope.Email, out var email);
        var externalPlanId = envelope.ExternalPlanId!.Trim();
        var maps = await db.GatewayPlans
            .Where(m => m.IsActive && m.Gateway.ToLower() == gatewayName.ToLower() && m.ExternalPlanId == externalPlanId)
            .ToListAsync(ct);
        if (maps.Count == 0) return (null, "no gateway plan mapping");
        if (maps.Count > 1) return (null, "ambiguous gateway plan mapping");
        var map = maps[0];

        var displayName = (envelope.CustomerName ?? "").Trim();
        if (displayName.Length > 200) displayName = displayName[..200];

        var customer = await CustomerStore.FindOrCreateAsync(db, email, displayName, gatewayName, ct);
        if (!string.IsNullOrEmpty(envelope.ExternalCustomerId) && string.IsNullOrEmpty(customer.ExternalCustomerId))
            customer.ExternalCustomerId = envelope.ExternalCustomerId;
        if (customer.Id == 0)
            await db.SaveChangesAsync(ct);

        var policy = await RefundPolicyGate.CurrentAsync(db, ct);
        var publicId = envelope.PublicId.Length is > 0 and <= 64
            ? envelope.PublicId
            : Guid.NewGuid().ToString("N");

        var order = new Order
        {
            PublicId = publicId,
            IdempotencyKey = IdempotencyKey.New(),
            CustomerId = customer.Id,
            ProductId = map.ProductId,
            PlanType = map.PlanCode,
            Amount = Money.Normalize(envelope.Amount!.Value),
            Currency = envelope.Currency!.Trim().ToUpperInvariant(),
            Gateway = gatewayName,
            Status = OrderStatuses.Pending,
            RefundPolicyId = policy?.Id,
            ExternalPaymentId = envelope.ExternalPaymentId,
            ExternalSubscriptionId = envelope.ExternalSubscriptionId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return (order, null);
    }

    public static void StampCharge(Order order, WebhookEnvelope envelope)
    {
        if (envelope.Amount is decimal amount && Money.IsNonNegative(amount))
            order.Amount = Money.Normalize(amount);
        var currency = (envelope.Currency ?? "").Trim();
        if (currency.Length is >= 3 and <= 8)
            order.Currency = currency.ToUpperInvariant();
        if (!string.IsNullOrEmpty(envelope.ExternalSubscriptionId) && string.IsNullOrEmpty(order.ExternalSubscriptionId))
            order.ExternalSubscriptionId = envelope.ExternalSubscriptionId;
    }
}
