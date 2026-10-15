using MasterServer.Catalog;
using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Invoices;

public static class InvoiceComposer
{
    public static async Task<InvoiceSnapshot> ComposeAsync(AppDb db, Invoice invoice, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == invoice.OrderId, ct);
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, ct);
        Product? product = null;
        if (order is not null)
            product = await db.Products.AsNoTracking().Include(p => p.Brand)
                .FirstOrDefaultAsync(p => p.Id == order.ProductId, ct);
        var brand = product?.Brand;
        var plan = await db.Plans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IsActive && p.Code.ToLower() == invoice.PlanType.ToLower(), ct);
        var logo = await BrandLogo.TryFetchAsync(brand?.LogoUrl, ct);

        return InvoicePdf.FromEntity(invoice) with
        {
            WebsiteUrl = brand?.WebsiteUrl ?? "",
            CustomerPhone = customer?.Phone ?? "",
            PlanLabel = InvoicePdf.PlanLabel(invoice.PlanType, plan?.DisplayName),
            PaidAt = order?.PaidAt ?? invoice.IssuedAt,
            ExternalPaymentId = order?.ExternalPaymentId,
            LogoBytes = logo,
            VoidedAt = invoice.VoidedAt
        };
    }
}
