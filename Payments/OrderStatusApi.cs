using MasterServer.Customers;
using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Payments;

/// <summary>
/// Public post-checkout status. Gateways return the buyer here; the page polls until the webhook
/// has paid or failed the matching order.
/// </summary>
public static class OrderStatusApi
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static void Map(WebApplication app)
    {
        app.MapGet("/v1/order/status", (HttpContext c, AppDb db, CancellationToken ct) => Get(c, db, ct));
    }

    private static async Task<IResult> Get(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var q = ctx.Request.Query;
        var publicId = First(q, "publicId", "public_id", "order");
        var paymentId = First(q, "payment_id", "paymentId", "payment");
        var subscriptionId = First(q, "subscription_id", "subscriptionId", "subscription");
        var licenseId = First(q, "license_id", "licenseId");
        var userId = First(q, "user_id", "userId");
        var urlStatus = First(q, "status", "redirect_status", "payment_status").ToLowerInvariant();
        var emailRaw = First(q, "email", "customer_email", "customerEmail", "user_email", "fs_email");
        EmailAddress.TryNormalize(emailRaw, out var email);

        if (!OrderStatusLookup.CanQuery(publicId, paymentId, subscriptionId, email, userId, urlStatus)
            && licenseId.Length == 0)
        {
            if (OrderStatusLookup.IsFailIntent(urlStatus))
                return Json(200, FailedView(urlStatus, "cancelled", "Payment was cancelled before it was completed."));
            return Fail(400, "payment_id, subscription_id, or email required");
        }

        var failIntent = OrderStatusLookup.IsFailIntent(urlStatus);
        var order = await FindAsync(
            db, publicId, paymentId, subscriptionId, licenseId, email, userId, allowEmailFallback: !failIntent, ct);
        if (order is null)
        {
            if (failIntent)
                return Json(200, FailedView(urlStatus, "cancelled", "Payment was cancelled before it was completed."));
            return Json(200, PendingView(email));
        }

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == order.ProductId, ct);
        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == order.PlanType, ct);
        return Json(200, StatusView(order, product, plan));
    }

    private static async Task<Order?> FindAsync(
        AppDb db,
        string publicId,
        string paymentId,
        string subscriptionId,
        string licenseId,
        string email,
        string userId,
        bool allowEmailFallback,
        CancellationToken ct)
    {
        IQueryable<Order> q = db.Orders.AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.License);

        if (publicId.Length > 0)
        {
            var byPublic = await q.FirstOrDefaultAsync(o => o.PublicId == publicId, ct);
            if (byPublic is not null) return byPublic;
        }

        if (paymentId.Length > 0)
        {
            var byPay = await q.Where(o =>
                    o.ExternalPaymentId == paymentId
                    || (o.License != null && o.License.ExternalPaymentId == paymentId))
                .OrderByDescending(o => o.Id)
                .FirstOrDefaultAsync(ct);
            if (byPay is not null) return byPay;
        }

        if (subscriptionId.Length > 0)
        {
            var bySub = await q.Where(o =>
                    o.ExternalSubscriptionId == subscriptionId
                    || (o.License != null && o.License.ExternalSubscriptionId == subscriptionId))
                .OrderByDescending(o => o.Id)
                .FirstOrDefaultAsync(ct);
            if (bySub is not null) return bySub;
        }

        if (licenseId.Length > 0)
        {
            var byLicense = await q.Where(o =>
                    o.ExternalCheckoutId == licenseId
                    || (o.License != null && o.License.ExternalSubscriptionId == licenseId)
                    || (o.License != null && o.License.ExternalPaymentId == licenseId))
                .OrderByDescending(o => o.Id)
                .FirstOrDefaultAsync(ct);
            if (byLicense is not null) return byLicense;
        }

        if (email.Length > 0 && userId.Length > 0)
        {
            var byUser = await q.Where(o =>
                    o.Customer.Email == email
                    && (o.Customer.ExternalCustomerId == userId
                        || (o.License != null && o.License.ExternalCustomerId == userId)))
                .OrderByDescending(o => o.Id)
                .FirstOrDefaultAsync(ct);
            if (byUser is not null) return byUser;
        }

        if (allowEmailFallback && email.Length > 0)
        {
            var since = DateTime.UtcNow.AddHours(-48);
            return await q.Where(o => o.Customer.Email == email && o.CreatedAt >= since)
                .OrderByDescending(o => o.Id)
                .FirstOrDefaultAsync(ct);
        }

        return null;
    }

    private static object StatusView(Order order, Product? product, Plan? plan)
    {
        var outcome = OrderStatusLookup.OutcomeFromOrderStatus(order.Status);
        var paid = outcome == "success";
        var maxMachines = order.License?.MaxMachines ?? plan?.DefaultMaxMachines ?? 0;
        return new
        {
            outcome,
            status = order.Status,
            reason = outcome is "failed" or "cancelled" ? order.Status : "",
            reasonCode = outcome is "failed" or "cancelled" ? order.Status : "",
            publicId = order.PublicId,
            orderNumber = order.PublicId,
            key = paid ? order.License?.Key ?? "" : "",
            licenseId = paid ? order.LicenseId : null,
            product = product?.Slug ?? "",
            productName = product?.DisplayName ?? "",
            downloadUrl = paid ? product?.DownloadUrl ?? "" : "",
            planType = order.PlanType,
            planName = plan?.DisplayName ?? order.PlanType,
            maxMachines,
            expiresAt = paid && order.License?.ExpiresAt is DateTime exp ? exp.ToString("o") : "",
            customerName = order.Customer?.DisplayName ?? "",
            email = order.Customer?.Email ?? "",
            phone = order.Customer?.Phone ?? "",
            gateway = order.Gateway,
            paymentMethod = OrderStatusLookup.PaymentMethod(order.Gateway),
            externalPaymentId = order.ExternalPaymentId ?? "",
            checkoutUrl = order.CheckoutUrl ?? "",
            purchasedAt = (order.PaidAt ?? order.CreatedAt).ToString("o"),
            createdAt = order.CreatedAt.ToString("o"),
            paidAt = order.PaidAt?.ToString("o") ?? "",
            amount = order.Amount,
            tax = 0m,
            discount = 0m,
            currency = order.Currency
        };
    }

    private static object PendingView(string email) => new
    {
        outcome = "pending",
        status = "",
        reason = "",
        reasonCode = "",
        email
    };

    private static object FailedView(string status, string reasonCode, string reason) => new
    {
        outcome = "failed",
        status,
        reason,
        reasonCode
    };

    private static string First(IQueryCollection q, params string[] names)
    {
        foreach (var name in names)
        {
            var value = q[name].ToString().Trim();
            if (value.Length > 0) return value;
        }
        return "";
    }

    private static IResult Fail(int status, string error) => Json(status, new { error });
    private static IResult Json(int status, object body) => Results.Json(body, JsonOptions, statusCode: status);
}
