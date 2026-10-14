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
            Body = "Purchases are refundable within 14 days of the most recent payment if you contact support. After 14 days, refunds are at the seller's discretion. Subscriptions may be cancelled at any time; unused time after the refund window is not automatically refunded. Accepting this policy is required before completing a purchase.",
            IsCurrent = true,
            EffectiveAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded default refund policy {Version}.", DefaultVersion);
    }
}
