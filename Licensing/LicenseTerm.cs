using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Licensing;

/// <summary>
/// The paid term starts at purchase (or gateway paid-at), not at first device activation.
/// Recurring gateway keys (<see cref="License.IsSubscription"/>) keep the expiry the webhook set.
/// </summary>
public static class LicenseTerm
{
    public static DateTime FromPurchase(DateTime purchasedAt, int durationDays) =>
        purchasedAt.AddDays(durationDays);

    public static async Task<DateTime> FromPurchaseAsync(AppDb db, string planType, DateTime purchasedAt, CancellationToken ct)
    {
        var days = await PlanCatalog.DurationDaysAsync(db, planType, ct);
        return FromPurchase(purchasedAt, days);
    }

    public static bool IsExpired(DateTime? expiresAt, DateTime now) =>
        expiresAt is DateTime exp && exp < now;

    public static async Task FillFromPurchaseAsync(AppDb db, ILogger logger, CancellationToken ct)
    {
        var licenses = await db.Licenses.Where(l => !l.IsSubscription).ToListAsync(ct);
        if (licenses.Count == 0) return;

        var purchasedByLicense = await db.Orders.AsNoTracking()
            .Where(o => o.LicenseId != null)
            .Select(o => new { o.LicenseId, At = o.PaidAt ?? o.CreatedAt })
            .ToListAsync(ct);
        var purchased = purchasedByLicense
            .GroupBy(x => x.LicenseId!.Value)
            .ToDictionary(g => g.Key, g => g.Max(x => x.At));

        var daysByPlan = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var changed = 0;
        foreach (var license in licenses)
        {
            if (!daysByPlan.TryGetValue(license.PlanType, out var days))
            {
                days = await PlanCatalog.DurationDaysAsync(db, license.PlanType, ct);
                daysByPlan[license.PlanType] = days;
            }
            var start = purchased.TryGetValue(license.Id, out var paidAt) ? paidAt : license.CreatedAt;
            var expires = FromPurchase(start, days);
            if (license.ExpiresAt == expires) continue;
            license.ExpiresAt = expires;
            changed++;
        }

        if (changed == 0) return;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Set expiry from purchase date on {Count} licenses.", changed);
    }
}
