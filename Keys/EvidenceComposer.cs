using MasterServer.Catalog;
using MasterServer.Data;
using MasterServer.Licensing;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Keys;

public static class EvidenceComposer
{

    public static async Task<EvidenceDocument> BuildAsync(AppDb db, License license, string generatedBy, CancellationToken ct)
    {
        var product = license.Product ?? await db.Products.AsNoTracking()
            .Include(p => p.Brand)
            .FirstOrDefaultAsync(p => p.Id == license.ProductId, ct);
        var brand = product?.Brand;
        if (brand is null && product?.BrandId is int brandId)
            brand = await db.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == brandId, ct);

        var customer = license.Customer ?? (license.CustomerId is int cid
            ? await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, ct)
            : null);
        if (customer is null && !string.IsNullOrWhiteSpace(license.CustomerEmail))
        {
            var email = license.CustomerEmail.ToLower();
            customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Email.ToLower() == email, ct);
        }

        var order = await db.Orders.AsNoTracking()
            .Where(o => o.LicenseId == license.Id)
            .OrderByDescending(o => o.Id)
            .FirstOrDefaultAsync(ct);
        Invoice? invoice = null;
        List<PaymentEvent> payments = [];
        if (order is not null)
        {
            invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.OrderId == order.Id, ct);
            payments = await db.PaymentEvents.AsNoTracking()
                .Where(e => e.OrderId == order.Id)
                .OrderBy(e => e.Id)
                .Take(40)
                .ToListAsync(ct);
        }

        var activations = license.Activations?.Count > 0
            ? license.Activations.OrderByDescending(a => a.ActivatedAt).ToList()
            : await db.Activations.AsNoTracking()
                .Where(a => a.LicenseId == license.Id)
                .OrderByDescending(a => a.ActivatedAt)
                .ToListAsync(ct);

        var heartbeats = await db.Heartbeats.AsNoTracking()
            .Where(h => h.LicenseId == license.Id)
            .OrderBy(h => h.SeenAt)
            .ToListAsync(ct);
        var heartbeatRows = heartbeats
            .GroupBy(h => h.MachineId)
            .Select(g => new EvidenceHeartbeatRow(
                g.Key,
                g.Last().AppVersion,
                g.Last().OsVersion,
                g.Min(x => x.SeenAt),
                g.Max(x => x.SeenAt),
                g.Count()))
            .OrderByDescending(r => r.LastSeen)
            .Take(40)
            .ToList();

        var mail = await db.MailOutbox.AsNoTracking()
            .Where(m => m.LicenseId == license.Id || (order != null && m.OrderId == order.Id))
            .OrderBy(m => m.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        var purchasedAt = order?.PaidAt ?? order?.CreatedAt ?? license.CreatedAt;
        var issuerName = FirstNonEmpty(brand?.Name, product?.DisplayName, "License");
        var logo = await BrandLogo.TryFetchAsync(brand?.LogoUrl, ct);
        var now = DateTime.UtcNow;

        return new EvidenceDocument
        {
            ReportId = $"EV-{now:yyyyMMddHHmmss}-{license.Id}",
            GeneratedAtUtc = now,
            GeneratedBy = generatedBy,
            IssuerName = issuerName,
            ProductName = product?.DisplayName ?? "",
            WebsiteUrl = brand?.WebsiteUrl ?? "",
            SupportEmail = brand?.SupportEmail ?? "",
            LogoBytes = logo,
            Key = license.Key,
            PlanType = license.PlanType,
            PlanLabel = PlanLabel(license.PlanType),
            MaxMachines = license.MaxMachines,
            Status = Status(license, now),
            PurchasedAt = purchasedAt,
            IssuedAt = license.CreatedAt,
            ActivatedAt = license.ActivatedAt,
            ExpiresAt = license.ExpiresAt,
            Source = string.IsNullOrWhiteSpace(license.Source) ? order?.Gateway ?? "" : license.Source,
            ExternalPaymentId = license.ExternalPaymentId ?? order?.ExternalPaymentId,
            ExternalSubscriptionId = license.ExternalSubscriptionId ?? order?.ExternalSubscriptionId,
            CustomerName = FirstNonEmpty(customer?.DisplayName, invoice?.CustomerName),
            CustomerEmail = FirstNonEmpty(license.CustomerEmail, customer?.Email, invoice?.CustomerEmail),
            OrderPublicId = order?.PublicId,
            OrderStatus = order?.Status,
            Amount = order?.Amount ?? invoice?.Amount,
            Currency = order?.Currency ?? invoice?.Currency,
            Gateway = order?.Gateway ?? invoice?.Gateway,
            PaidAt = order?.PaidAt,
            InvoiceNumber = invoice?.Number,
            InvoiceStatus = invoice?.Status,
            InvoiceSha = invoice?.PdfSha256,
            Mail = mail.Select(m => new EvidenceMailRow(m.Template, m.ToEmail, m.Status, m.CreatedAt, m.SentAt)).ToList(),
            Activations = activations.Select(a => new EvidenceActivationRow(
                a.MachineId, a.IpAddress, a.AppVersion, a.OsVersion, a.ActivatedAt, a.LastSeenAt, a.DeactivatedAt)).ToList(),
            Heartbeats = heartbeatRows,
            Payments = payments.Select(p => new EvidencePaymentRow(p.Gateway, p.EventType, p.PayloadSha256, p.CreatedAt)).ToList()
        };
    }

    public static Task<byte[]?> TryLogoAsync(string? logoUrl, CancellationToken ct) =>
        BrandLogo.TryFetchAsync(logoUrl, ct);

    private static string Status(License l, DateTime now)
    {
        if (l.IsRevoked) return "revoked";
        if (!l.IsActive) return "inactive";
        if (LicenseTerm.IsExpired(l.ExpiresAt, now)) return "expired";
        if (l.ActivatedAt is null) return "unused";
        return "active";
    }

    private static string PlanLabel(string plan) => plan.Trim().ToLowerInvariant() switch
    {
        "monthly" => "Monthly",
        "1year" => "1 Year",
        "2year" => "2 Years",
        "3year" => "3 Years",
        "5year" => "5 Years",
        "lifetime" => "Lifetime",
        _ => plan
    };

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        return "";
    }
}
