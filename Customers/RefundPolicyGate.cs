using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Customers;

/// <summary>
/// Phase 5 purchases call this before creating an order. If no current policy exists,
/// acceptance is not required.
/// </summary>
public static class RefundPolicyGate
{
    public static async Task<RefundPolicy?> CurrentAsync(AppDb db, CancellationToken ct) =>
        await db.RefundPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.IsCurrent, ct);

    public static async Task<bool> HasAcceptedCurrentAsync(AppDb db, int customerId, CancellationToken ct)
    {
        var current = await db.RefundPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.IsCurrent, ct);
        if (current is null) return true;
        return await db.RefundPolicyAcceptances
            .AsNoTracking()
            .AnyAsync(a => a.CustomerId == customerId && a.PolicyId == current.Id, ct);
    }
}
