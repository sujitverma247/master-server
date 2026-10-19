using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Customers;

/// <summary>Seeds a generic current refund policy when none exist. Brand-neutral; body is not product-specific.</summary>
public static class RefundPolicySeed
{
    public const string DefaultVersion = "2026-10-v1";

    public static async Task ApplyAsync(AppDb db, ILogger logger, CancellationToken ct)
    {
        if (await db.RefundPolicies.AnyAsync(ct))
            return;

        db.RefundPolicies.Add(new RefundPolicy
        {
            Version = DefaultVersion,
            Title = "Refund policy",
            Body = "Personal software purchases are refundable within 30 calendar days of the initial payment if you contact support. After 30 days, refunds are at the seller's discretion unless applicable law requires otherwise. Subscriptions may be cancelled at any time; unused time after the refund window is not automatically refunded. Payment partners may process the refund on the original payment method. Accepting this policy is required before completing a purchase. The full Refund Policy is published at https://europedigisystems.com/legal/refund.",
            IsCurrent = true,
            EffectiveAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded default refund policy {Version}.", DefaultVersion);
    }
}
