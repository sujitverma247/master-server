using MasterServer.Customers;
using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Mail;

public static class MailOutbox
{
    public static async Task EnqueueLicenseIssuedAsync(AppDb db, License license, Order? order, CancellationToken ct)
    {
        var idem = order is not null ? $"{MailTemplates.LicenseIssued}:order#{order.Id}" : $"{MailTemplates.LicenseIssued}:license#{license.Id}";
        await EnqueueAsync(db, MailTemplates.LicenseIssued, idem, license, order, ct);
    }

    public static async Task EnqueueOrderRefundedAsync(AppDb db, Order order, License? license, CancellationToken ct)
    {
        var idem = $"{MailTemplates.OrderRefunded}:order#{order.Id}";
        await EnqueueAsync(db, MailTemplates.OrderRefunded, idem, license, order, ct);
    }

    private static async Task EnqueueAsync(AppDb db, string template, string idem, License? license, Order? order, CancellationToken ct)
    {
        if (await db.MailOutbox.AnyAsync(m => m.IdempotencyKey == idem, ct))
            return;

        var productId = license?.ProductId ?? order?.ProductId;
        Product? product = null;
        if (productId is int pid)
            product = await db.Products.Include(p => p.Brand).FirstOrDefaultAsync(p => p.Id == pid, ct);

        Customer? customer = null;
        var customerId = license?.CustomerId ?? order?.CustomerId;
        if (customerId is int cid)
            customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == cid, ct);

        var toEmail = customer?.Email ?? license?.CustomerEmail ?? "";
        if (!EmailAddress.TryNormalize(toEmail, out var email))
            return;

        var brand = product?.Brand;
        var support = brand?.SupportEmail is { Length: > 0 } s && EmailAddress.TryNormalize(s, out var se)
            ? se : MailSenderFactory.DefaultFromEmail();
        var fromName = brand is { Name.Length: > 0 } ? brand.Name : MailSenderFactory.DefaultFromName();
        if (!EmailAddress.TryNormalize(support, out var fromEmail))
            fromEmail = MailSenderFactory.DefaultFromEmail();

        var vars = new MailVars(
            ProductName: product?.DisplayName is { Length: > 0 } pn ? pn : (product?.Slug ?? "your product"),
            BrandName: brand?.Name is { Length: > 0 } bn ? bn : fromName,
            SupportEmail: fromEmail.Length > 0 ? fromEmail : support,
            CustomerName: customer?.DisplayName is { Length: > 0 } dn ? dn : "",
            CustomerEmail: email,
            LicenseKey: license?.Key ?? "",
            PlanType: license?.PlanType ?? order?.PlanType ?? "",
            OrderPublicId: order?.PublicId ?? "");

        var locale = MailLocale.Normalize(customer?.Locale);
        var rendered = MailRenderer.Render(template, vars, locale);
        db.MailOutbox.Add(new OutboundMail
        {
            Template = template,
            IdempotencyKey = idem,
            ToEmail = email,
            ToName = vars.CustomerName.Length > 0 ? vars.CustomerName : email,
            FromEmail = fromEmail,
            FromName = fromName,
            Subject = rendered.Subject,
            Body = rendered.Body,
            Locale = locale,
            Status = MailStatuses.Pending,
            ProductId = product?.Id,
            CustomerId = customer?.Id,
            OrderId = order?.Id,
            LicenseId = license?.Id,
            CreatedAt = DateTime.UtcNow
        });
    }
}
