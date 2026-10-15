using MasterServer.Catalog;
using MasterServer.Data;
using MasterServer.Mail;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MasterServer.Invoices;

public static class InvoiceIssuer
{
    public static async Task<Invoice> IssueForOrderAsync(AppDb db, Order order, CancellationToken ct)
    {
        var existing = await db.Invoices.FirstOrDefaultAsync(i => i.OrderId == order.Id, ct);
        if (existing is not null) return existing;
        if (order.Status != OrderStatuses.Paid)
            throw new InvalidOperationException("invoice requires a paid order");

        var customer = order.Customer ?? await db.Customers.FirstAsync(c => c.Id == order.CustomerId, ct);
        var product = await db.Products.Include(p => p.Brand).FirstOrDefaultAsync(p => p.Id == order.ProductId, ct);
        var brand = product?.Brand;
        var policyVersion = "";
        if (order.RefundPolicyId is int pid)
        {
            var policy = await db.RefundPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid, ct);
            policyVersion = policy?.Version ?? "";
        }

        var issuedAt = DateTime.UtcNow;
        var support = brand?.SupportEmail is { Length: > 0 } se ? se : MailSenderFactory.DefaultFromEmail();
        var plan = await db.Plans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IsActive && p.Code.ToLower() == order.PlanType.ToLower(), ct);
        var logo = await BrandLogo.TryFetchAsync(brand?.LogoUrl, ct);
        var extras = (
            CustomerEmail: customer.Email,
            CustomerName: customer.DisplayName.Length > 0 ? customer.DisplayName : customer.Email,
            CustomerPhone: customer.Phone,
            ProductName: product?.DisplayName is { Length: > 0 } pn ? pn : (product?.Slug ?? "product"),
            BrandName: brand?.Name is { Length: > 0 } bn ? bn : "Seller",
            SupportEmail: support,
            WebsiteUrl: brand?.WebsiteUrl ?? "",
            PlanLabel: InvoicePdf.PlanLabel(order.PlanType, plan?.DisplayName),
            LogoBytes: logo
        );

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var number = await NextNumberAsync(db, issuedAt, ct);
            var invoice = Build(order, number, issuedAt, policyVersion, extras);
            db.Invoices.Add(invoice);
            try
            {
                await db.SaveChangesAsync(ct);
                return invoice;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
                && pg.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                db.Entry(invoice).State = EntityState.Detached;
                var raced = await db.Invoices.FirstOrDefaultAsync(i => i.OrderId == order.Id, ct);
                if (raced is not null) return raced;
            }
        }

        throw new InvalidOperationException("could not allocate an invoice number");
    }

    public static async Task VoidForOrderAsync(AppDb db, int orderId, CancellationToken ct)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.OrderId == orderId, ct);
        if (invoice is null || invoice.Status == InvoiceStatuses.Void) return;
        invoice.Status = InvoiceStatuses.Void;
        invoice.VoidedAt = DateTime.UtcNow;
    }

    private static Invoice Build(Order order, string number, DateTime issuedAt, string policyVersion,
        (string CustomerEmail, string CustomerName, string CustomerPhone, string ProductName, string BrandName,
            string SupportEmail, string WebsiteUrl, string PlanLabel, byte[]? LogoBytes) names)
    {
        var invoice = new Invoice
        {
            PublicId = Guid.NewGuid().ToString("N"),
            Number = number,
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            CustomerEmail = names.CustomerEmail,
            CustomerName = names.CustomerName,
            ProductName = names.ProductName,
            BrandName = names.BrandName,
            SupportEmail = names.SupportEmail,
            PlanType = order.PlanType,
            Amount = order.Amount,
            Currency = order.Currency,
            Gateway = order.Gateway,
            OrderPublicId = order.PublicId,
            PolicyVersion = policyVersion,
            Status = InvoiceStatuses.Issued,
            IssuedAt = issuedAt
        };
        var snapshot = InvoicePdf.FromEntity(invoice) with
        {
            WebsiteUrl = names.WebsiteUrl,
            CustomerPhone = names.CustomerPhone,
            PlanLabel = names.PlanLabel,
            PaidAt = order.PaidAt ?? issuedAt,
            ExternalPaymentId = order.ExternalPaymentId,
            LogoBytes = names.LogoBytes
        };
        var pdf = InvoicePdf.Render(snapshot);
        invoice.PdfBytes = pdf;
        invoice.PdfSha256 = InvoicePdf.Sha256Hex(pdf);
        return invoice;
    }

    private static async Task<string> NextNumberAsync(AppDb db, DateTime issuedAt, CancellationToken ct)
    {
        var start = DateTime.SpecifyKind(issuedAt.Date, DateTimeKind.Utc);
        var end = start.AddDays(1);
        var count = await db.Invoices.CountAsync(i => i.IssuedAt >= start && i.IssuedAt < end, ct);
        return InvoiceNumber.Format(start, count + 1);
    }
}
