using MasterServer.Data;
using MasterServer.Invoices;
using MasterServer.Keys;
using MasterServer.Mail;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Payments;

public static class OrderFulfillment
{
    public static async Task<string?> ApplyAsync(AppDb db, Order order, string status,
        string? externalPaymentId, CancellationToken ct)
    {
        status = status.Trim().ToLowerInvariant();
        return status switch
        {
            OrderStatuses.Paid => await PayAsync(db, order, externalPaymentId, ct),
            OrderStatuses.Failed => Fail(order, OrderStatuses.Failed),
            OrderStatuses.Cancelled => Fail(order, OrderStatuses.Cancelled),
            OrderStatuses.Refunded => await RefundAsync(db, order, ct),
            _ => "unknown status"
        };
    }

    private static async Task<string?> PayAsync(AppDb db, Order order, string? externalPaymentId, CancellationToken ct)
    {
        if (order.Status == OrderStatuses.Refunded) return "order refunded";
        if (order.Status == OrderStatuses.Paid && order.LicenseId is not null)
        {
            await InvoiceIssuer.IssueForOrderAsync(db, order, ct);
            return null;
        }

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == order.ProductId, ct);
        if (product is null) return "unknown product";
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == order.CustomerId, ct);
        if (customer is null) return "unknown customer";

        var plan = await db.Plans.FirstOrDefaultAsync(p => p.IsActive && p.Code == order.PlanType, ct);
        var maxMachines = plan?.DefaultMaxMachines ?? 3;

        string key;
        do { key = KeyGenerator.Generate(product.KeyPrefix); }
        while (await db.Licenses.AnyAsync(l => l.Key == key, ct));

        var license = new License
        {
            Key = key,
            ProductId = order.ProductId,
            PlanType = order.PlanType,
            MaxMachines = maxMachines,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = null,
            IsActive = true,
            IsRevoked = false,
            CustomerId = customer.Id,
            CustomerEmail = customer.Email,
            Source = order.Gateway,
            ExternalPaymentId = externalPaymentId,
            ExternalCustomerId = customer.ExternalCustomerId
        };
        db.Licenses.Add(license);
        await db.SaveChangesAsync(ct);

        order.LicenseId = license.Id;
        order.Status = OrderStatuses.Paid;
        order.PaidAt = DateTime.UtcNow;
        order.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(externalPaymentId))
            order.ExternalPaymentId = externalPaymentId;
        await MailOutbox.EnqueueLicenseIssuedAsync(db, license, order, ct);
        await InvoiceIssuer.IssueForOrderAsync(db, order, ct);
        return null;
    }

    private static string? Fail(Order order, string status)
    {
        if (order.Status == OrderStatuses.Paid || order.Status == OrderStatuses.Refunded)
            return null;
        order.Status = status;
        order.UpdatedAt = DateTime.UtcNow;
        return null;
    }

    private static async Task<string?> RefundAsync(AppDb db, Order order, CancellationToken ct)
    {
        if (order.Status == OrderStatuses.Refunded) return null;
        if (order.Status != OrderStatuses.Paid) return "order is not paid";

        License? license = null;
        if (order.LicenseId is int lid)
        {
            license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == lid, ct);
            if (license is not null) license.IsRevoked = true;
        }
        order.Status = OrderStatuses.Refunded;
        order.UpdatedAt = DateTime.UtcNow;
        await MailOutbox.EnqueueOrderRefundedAsync(db, order, license, ct);
        await InvoiceIssuer.VoidForOrderAsync(db, order.Id, ct);
        return null;
    }
}
