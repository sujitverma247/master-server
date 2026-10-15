using MasterServer.Admin;
using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Analytics;

public static class AnalyticsApi
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static void Map(WebApplication app)
    {
        app.MapGet("/admin/analytics/overview", (HttpContext c, AppDb db, CancellationToken ct) => Overview(c, db, ct));
    }

    private static async Task<IResult> Overview(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.AnalyticsRead, ct);
        if (g.Result is not null) return g.Result;

        DateTime from, toExclusive, previousFrom;
        int days;
        var fromRaw = ctx.Request.Query["from"].ToString().Trim();
        var toRaw = ctx.Request.Query["to"].ToString().Trim();
        if (fromRaw.Length > 0 || toRaw.Length > 0)
        {
            if (!AnalyticsWindow.TryParseDay(fromRaw, out var fromDay) || !AnalyticsWindow.TryParseDay(toRaw, out var toDay))
                return Fail(400, "invalid from or to (use yyyy-MM-dd)");
            var invalid = AnalyticsWindow.ValidateCustom(fromDay, toDay);
            if (invalid is not null) return Fail(400, invalid);
            (from, toExclusive, previousFrom, days) = AnalyticsWindow.Custom(fromDay, toDay);
        }
        else
        {
            days = AnalyticsWindow.DefaultDays;
            if (int.TryParse(ctx.Request.Query["days"], out var raw))
                days = AnalyticsWindow.ClampDays(raw);
            (from, toExclusive, previousFrom) = AnalyticsWindow.Bounds(DateTime.UtcNow, days);
        }

        var buckets = AnalyticsWindow.Series(from, toExclusive);

        var licensesTotal = await db.Licenses.AsNoTracking().CountAsync(ct);
        var licensesNow = await db.Licenses.AsNoTracking().CountAsync(l => l.CreatedAt >= from && l.CreatedAt < toExclusive, ct);
        var licensesPrev = await db.Licenses.AsNoTracking().CountAsync(l => l.CreatedAt >= previousFrom && l.CreatedAt < from, ct);

        var paidTotal = await db.Orders.AsNoTracking().CountAsync(o => o.Status == OrderStatuses.Paid, ct);
        var paidNow = await db.Orders.AsNoTracking().CountAsync(o =>
            o.Status == OrderStatuses.Paid && (o.PaidAt ?? o.CreatedAt) >= from && (o.PaidAt ?? o.CreatedAt) < toExclusive, ct);
        var paidPrev = await db.Orders.AsNoTracking().CountAsync(o =>
            o.Status == OrderStatuses.Paid && (o.PaidAt ?? o.CreatedAt) >= previousFrom && (o.PaidAt ?? o.CreatedAt) < from, ct);

        var customersTotal = await db.Customers.AsNoTracking().CountAsync(ct);
        var customersNow = await db.Customers.AsNoTracking().CountAsync(c => c.CreatedAt >= from && c.CreatedAt < toExclusive, ct);
        var customersPrev = await db.Customers.AsNoTracking().CountAsync(c => c.CreatedAt >= previousFrom && c.CreatedAt < from, ct);

        var openTickets = await db.Tickets.AsNoTracking().CountAsync(t => t.Status == TicketStatuses.Open, ct);
        var ticketsNow = await db.Tickets.AsNoTracking().CountAsync(t => t.CreatedAt >= from && t.CreatedAt < toExclusive, ct);
        var ticketsPrev = await db.Tickets.AsNoTracking().CountAsync(t => t.CreatedAt >= previousFrom && t.CreatedAt < from, ct);

        var pendingMail = await db.MailOutbox.AsNoTracking().CountAsync(m => m.Status == MailStatuses.Pending, ct);
        var mailNow = await db.MailOutbox.AsNoTracking().CountAsync(m => m.CreatedAt >= from && m.CreatedAt < toExclusive, ct);
        var mailPrev = await db.MailOutbox.AsNoTracking().CountAsync(m => m.CreatedAt >= previousFrom && m.CreatedAt < from, ct);

        var paidTimes = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatuses.Paid && (o.PaidAt ?? o.CreatedAt) >= from && (o.PaidAt ?? o.CreatedAt) < toExclusive)
            .Select(o => o.PaidAt ?? o.CreatedAt)
            .ToListAsync(ct);
        var activationTimes = await db.Activations.AsNoTracking()
            .Where(a => a.ActivatedAt >= from && a.ActivatedAt < toExclusive)
            .Select(a => a.ActivatedAt)
            .ToListAsync(ct);

        var paidByDay = AnalyticsWindow.Bucket(paidTimes, buckets);
        var activationsByDay = AnalyticsWindow.Bucket(activationTimes, buckets);

        var recentOrders = await db.Orders.AsNoTracking()
            .Include(o => o.Customer)
            .OrderByDescending(o => o.Id)
            .Take(5)
            .ToListAsync(ct);
        var recentTickets = await db.Tickets.AsNoTracking()
            .OrderByDescending(t => t.UpdatedAt)
            .Take(5)
            .ToListAsync(ct);

        return Json(200, new
        {
            days,
            from = from.ToString("o"),
            to = toExclusive.AddTicks(-1).ToString("o"),
            kpis = new
            {
                licenses = KpiPct(licensesTotal, licensesNow, licensesPrev),
                paidOrders = KpiPct(paidTotal, paidNow, paidPrev),
                customers = KpiPct(customersTotal, customersNow, customersPrev),
                openTickets = KpiAbs(openTickets, ticketsNow, ticketsPrev),
                pendingMail = KpiAbs(pendingMail, mailNow, mailPrev)
            },
            series = new
            {
                labels = buckets.Select(b => b.Label),
                paidOrders = paidByDay,
                activations = activationsByDay
            },
            recentOrders = recentOrders.Select(o => new
            {
                o.Id,
                o.PublicId,
                customerEmail = o.Customer?.Email ?? "",
                amount = o.Amount,
                o.Currency,
                o.Status,
                createdAt = o.CreatedAt.ToString("o"),
                paidAt = o.PaidAt?.ToString("o")
            }),
            recentTickets = recentTickets.Select(t => new
            {
                t.Id,
                t.PublicId,
                t.Subject,
                t.Status,
                updatedAt = t.UpdatedAt.ToString("o")
            })
        });
    }

    private static object KpiPct(int total, int current, int previous) => new
    {
        total, current, previous,
        deltaPct = AnalyticsWindow.DeltaPct(current, previous)
    };

    private static object KpiAbs(int total, int current, int previous) => new
    {
        total, current, previous,
        delta = AnalyticsWindow.Delta(current, previous)
    };

    private readonly record struct Gate(IResult? Result, AdminPrincipal? Principal);

    private static async Task<Gate> Require(HttpContext ctx, AppDb db, string permission, CancellationToken ct)
    {
        var principal = await AdminAuth.ResolveAsync(ctx, db, ct);
        if (principal is null) return new Gate(Fail(401, "unauthorized"), null);
        if (!principal.Has(permission)) return new Gate(Fail(403, "forbidden"), null);
        return new Gate(null, principal);
    }

    private static IResult Fail(int status, string error) => Json(status, new { error });
    private static IResult Json(int status, object body) => Results.Json(body, JsonOptions, statusCode: status);
}
