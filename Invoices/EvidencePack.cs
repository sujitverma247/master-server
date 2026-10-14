using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Invoices;

/// <summary>
/// Purchase evidence: invoice hash, policy acceptance, and payment-event hashes.
/// Never includes a license key or a raw webhook payload.
/// </summary>
public static class EvidencePack
{
    public static async Task<object> BuildAsync(AppDb db, Order order, bool includeLicenseId, CancellationToken ct)
    {
        var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.OrderId == order.Id, ct);
        RefundPolicyAcceptance? acceptance = null;
        RefundPolicy? policy = null;
        if (order.RefundPolicyId is int pid)
        {
            policy = await db.RefundPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid, ct);
            acceptance = await db.RefundPolicyAcceptances.AsNoTracking()
                .FirstOrDefaultAsync(a => a.CustomerId == order.CustomerId && a.PolicyId == pid, ct);
        }

        var events = await db.PaymentEvents.AsNoTracking()
            .Where(e => e.OrderId == order.Id)
            .OrderBy(e => e.Id)
            .ToListAsync(ct);

        return new
        {
            order = new
            {
                publicId = order.PublicId,
                order.Status,
                order.PlanType,
                amount = order.Amount,
                order.Currency,
                order.Gateway,
                licenseId = includeLicenseId ? order.LicenseId : null,
                paidAt = order.PaidAt?.ToString("o"),
                createdAt = order.CreatedAt.ToString("o")
            },
            invoice = invoice is null ? null : new
            {
                invoice.PublicId,
                invoice.Number,
                invoice.Status,
                invoice.PdfSha256,
                issuedAt = invoice.IssuedAt.ToString("o"),
                voidedAt = invoice.VoidedAt?.ToString("o")
            },
            policy = policy is null ? null : new
            {
                policy.Version,
                policy.Title,
                acceptedAt = acceptance?.AcceptedAt.ToString("o"),
                source = acceptance?.Source ?? ""
            },
            payments = events.Select(e => new
            {
                e.Gateway,
                e.EventType,
                e.PayloadSha256,
                createdAt = e.CreatedAt.ToString("o")
            })
        };
    }
}
