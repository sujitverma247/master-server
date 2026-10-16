using MasterServer.Admin;
using MasterServer.Catalog;
using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Telemetry;

public static class TelemetryApi
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static void Map(WebApplication app)
    {
        app.MapGet("/admin/machines", (HttpContext c, AppDb db, CancellationToken ct) => ListMachines(c, db, ct));
        app.MapGet("/admin/machines/{id:int}", (int id, HttpContext c, AppDb db, CancellationToken ct) => GetMachine(id, c, db, ct));
        app.MapGet("/admin/heartbeats", (HttpContext c, AppDb db, CancellationToken ct) => ListHeartbeats(c, db, ct));
    }

    private static async Task<IResult> ListMachines(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.TelemetryRead, ct);
        if (g.Result is not null) return g.Result;

        var stale = MachinePresence.StaleAfterFromEnvironment();
        var now = DateTime.UtcNow;
        var cutoff = now - stale;

        var q =
            from a in db.Activations.AsNoTracking()
            join l in db.Licenses.AsNoTracking() on a.LicenseId equals l.Id
            join p in db.Products.AsNoTracking() on l.ProductId equals p.Id
            select new { a, l, p };

        var machineId = ctx.Request.Query["machineId"].ToString().Trim();
        if (machineId.Length > 0) q = q.Where(x => x.a.MachineId == machineId);
        if (int.TryParse(ctx.Request.Query["licenseId"], out var lid)) q = q.Where(x => x.a.LicenseId == lid);
        if (int.TryParse(ctx.Request.Query["productId"], out var pid)) q = q.Where(x => x.l.ProductId == pid);

        var status = ctx.Request.Query["status"].ToString().Trim().ToLowerInvariant();
        var seatedOnly = ctx.Request.Query["seated"].ToString() == "1" || status is "seated";
        if (status is "online")
            q = q.Where(x => x.a.DeactivatedAt == null && (x.a.LastSeenAt ?? x.a.ActivatedAt) >= cutoff);
        else if (status is "offline")
            q = q.Where(x => x.a.DeactivatedAt == null && (x.a.LastSeenAt ?? x.a.ActivatedAt) < cutoff);
        else if (status is "released" or "unseated" or "inactive")
            q = q.Where(x => x.a.DeactivatedAt != null);
        else if (seatedOnly)
            q = q.Where(x => x.a.DeactivatedAt == null);

        var search = ctx.Request.Query["q"].ToString().Trim();
        if (search.Length > 0)
        {
            var s = search.ToLowerInvariant();
            q = q.Where(x =>
                x.a.MachineId.ToLower().Contains(s)
                || x.a.IpAddress.Contains(s)
                || x.a.AppVersion.ToLower().Contains(s)
                || x.a.OsVersion.ToLower().Contains(s)
                || x.l.Key.ToLower().Contains(s)
                || x.l.CustomerEmail.ToLower().Contains(s)
                || x.p.DisplayName.ToLower().Contains(s));
        }

        if (DateOnly.TryParse(ctx.Request.Query["from"], out var fromDay))
        {
            var fromUtc = fromDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(x => x.a.ActivatedAt >= fromUtc);
        }
        if (DateOnly.TryParse(ctx.Request.Query["to"], out var toDay))
        {
            var toExclusive = toDay.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(x => x.a.ActivatedAt < toExclusive);
        }

        var sort = ctx.Request.Query["sort"].ToString().Trim().ToLowerInvariant();
        var asc = ctx.Request.Query["dir"].ToString().Trim().Equals("asc", StringComparison.OrdinalIgnoreCase);
        q = sort switch
        {
            "id" => asc ? q.OrderBy(x => x.a.Id) : q.OrderByDescending(x => x.a.Id),
            "machine" or "machineid" => asc ? q.OrderBy(x => x.a.MachineId) : q.OrderByDescending(x => x.a.MachineId),
            "product" => asc ? q.OrderBy(x => x.p.DisplayName) : q.OrderByDescending(x => x.p.DisplayName),
            "key" => asc ? q.OrderBy(x => x.l.Key) : q.OrderByDescending(x => x.l.Key),
            "os" or "osversion" => asc ? q.OrderBy(x => x.a.OsVersion) : q.OrderByDescending(x => x.a.OsVersion),
            "ip" or "ipaddress" => asc ? q.OrderBy(x => x.a.IpAddress) : q.OrderByDescending(x => x.a.IpAddress),
            "status" => asc
                ? q.OrderBy(x => x.a.DeactivatedAt != null).ThenBy(x => x.a.LastSeenAt ?? x.a.ActivatedAt)
                : q.OrderByDescending(x => x.a.DeactivatedAt != null).ThenByDescending(x => x.a.LastSeenAt ?? x.a.ActivatedAt),
            "activated" or "activatedat" => asc ? q.OrderBy(x => x.a.ActivatedAt) : q.OrderByDescending(x => x.a.ActivatedAt),
            _ => asc
                ? q.OrderBy(x => x.a.LastSeenAt ?? x.a.ActivatedAt)
                : q.OrderByDescending(x => x.a.LastSeenAt ?? x.a.ActivatedAt)
        };

        var total = await q.CountAsync(ct);
        var limit = CatalogPaging.NormalizeLimit(ctx.Request.Query["limit"].ToString());
        var page = CatalogPaging.ClampPage(CatalogPaging.NormalizePage(ctx.Request.Query["page"].ToString()), total, limit);
        var rows = await q.Skip((page - 1) * limit).Take(limit).ToListAsync(ct);

        var allCount = await db.Activations.CountAsync(ct);
        var seatedCount = await db.Activations.CountAsync(a => a.DeactivatedAt == null, ct);
        var onlineCount = await db.Activations.CountAsync(
            a => a.DeactivatedAt == null && (a.LastSeenAt ?? a.ActivatedAt) >= cutoff, ct);

        var products = await db.Activations.AsNoTracking()
            .Join(db.Licenses.AsNoTracking(), a => a.LicenseId, l => l.Id, (a, l) => l.ProductId)
            .Distinct()
            .Join(db.Products.AsNoTracking(), id => id, p => p.Id, (id, p) => new { p.Id, name = p.DisplayName })
            .OrderBy(x => x.name)
            .ToListAsync(ct);

        return Json(200, new
        {
            machines = rows.Select(x => MachineView(x.a, x.l, x.p, now, stale)),
            total,
            page,
            limit,
            staleHours = (int)stale.TotalHours,
            counts = new
            {
                total = allCount,
                seated = seatedCount,
                online = onlineCount,
                offline = seatedCount - onlineCount
            },
            products
        });
    }

    private static async Task<IResult> GetMachine(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.TelemetryRead, ct);
        if (g.Result is not null) return g.Result;

        var row = await (
            from a in db.Activations.AsNoTracking()
            join l in db.Licenses.AsNoTracking() on a.LicenseId equals l.Id
            join p in db.Products.AsNoTracking() on l.ProductId equals p.Id
            where a.Id == id
            select new { a, l, p }
        ).FirstOrDefaultAsync(ct);
        if (row is null) return Fail(404, "machine not found");

        var stale = MachinePresence.StaleAfterFromEnvironment();
        var now = DateTime.UtcNow;
        var heartbeats = await db.Heartbeats.AsNoTracking()
            .Where(h => h.ActivationId == id)
            .OrderByDescending(h => h.SeenAt)
            .Take(20)
            .Select(h => new
            {
                h.Id, h.LicenseId, h.ActivationId, h.MachineId, h.AppVersion, h.OsVersion, h.IpAddress,
                seenAt = h.SeenAt.ToString("o")
            })
            .ToListAsync(ct);

        return Json(200, new
        {
            machine = MachineView(row.a, row.l, row.p, now, stale),
            staleHours = (int)stale.TotalHours,
            heartbeats
        });
    }

    private const int HeartbeatRetentionDays = 14;

    private static async Task<IResult> ListHeartbeats(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.TelemetryRead, ct);
        if (g.Result is not null) return g.Result;

        var q =
            from h in db.Heartbeats.AsNoTracking()
            join l in db.Licenses.AsNoTracking() on h.LicenseId equals l.Id
            join p in db.Products.AsNoTracking() on l.ProductId equals p.Id
            select new { h, l, p };

        var machineId = ctx.Request.Query["machineId"].ToString().Trim();
        if (machineId.Length > 0) q = q.Where(x => x.h.MachineId == machineId);
        if (int.TryParse(ctx.Request.Query["licenseId"], out var lid)) q = q.Where(x => x.h.LicenseId == lid);
        if (int.TryParse(ctx.Request.Query["activationId"], out var aid)) q = q.Where(x => x.h.ActivationId == aid);
        if (int.TryParse(ctx.Request.Query["productId"], out var pid)) q = q.Where(x => x.l.ProductId == pid);

        var search = ctx.Request.Query["q"].ToString().Trim();
        if (search.Length > 0)
        {
            var s = search.ToLowerInvariant();
            q = q.Where(x =>
                x.h.MachineId.ToLower().Contains(s)
                || x.h.IpAddress.Contains(s)
                || x.h.AppVersion.ToLower().Contains(s)
                || x.h.OsVersion.ToLower().Contains(s)
                || x.l.Key.ToLower().Contains(s)
                || x.l.CustomerEmail.ToLower().Contains(s)
                || x.p.DisplayName.ToLower().Contains(s));
        }

        if (DateOnly.TryParse(ctx.Request.Query["from"], out var fromDay))
        {
            var fromUtc = fromDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(x => x.h.SeenAt >= fromUtc);
        }
        if (DateOnly.TryParse(ctx.Request.Query["to"], out var toDay))
        {
            var toExclusive = toDay.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(x => x.h.SeenAt < toExclusive);
        }

        var sort = ctx.Request.Query["sort"].ToString().Trim().ToLowerInvariant();
        var asc = ctx.Request.Query["dir"].ToString().Trim().Equals("asc", StringComparison.OrdinalIgnoreCase);
        q = sort switch
        {
            "id" => asc ? q.OrderBy(x => x.h.Id) : q.OrderByDescending(x => x.h.Id),
            "machine" or "machineid" => asc ? q.OrderBy(x => x.h.MachineId) : q.OrderByDescending(x => x.h.MachineId),
            "product" => asc ? q.OrderBy(x => x.p.DisplayName) : q.OrderByDescending(x => x.p.DisplayName),
            "key" => asc ? q.OrderBy(x => x.l.Key) : q.OrderByDescending(x => x.l.Key),
            "os" or "osversion" => asc ? q.OrderBy(x => x.h.OsVersion) : q.OrderByDescending(x => x.h.OsVersion),
            "ip" or "ipaddress" => asc ? q.OrderBy(x => x.h.IpAddress) : q.OrderByDescending(x => x.h.IpAddress),
            "app" or "appversion" => asc ? q.OrderBy(x => x.h.AppVersion) : q.OrderByDescending(x => x.h.AppVersion),
            _ => asc ? q.OrderBy(x => x.h.SeenAt) : q.OrderByDescending(x => x.h.SeenAt)
        };

        var total = await q.CountAsync(ct);
        var limit = CatalogPaging.NormalizeLimit(ctx.Request.Query["limit"].ToString());
        var page = CatalogPaging.ClampPage(CatalogPaging.NormalizePage(ctx.Request.Query["page"].ToString()), total, limit);
        var rows = await q.Skip((page - 1) * limit).Take(limit).ToListAsync(ct);

        var now = DateTime.UtcNow;
        var last24h = await db.Heartbeats.CountAsync(h => h.SeenAt >= now.AddHours(-24), ct);
        var lastHour = await db.Heartbeats.CountAsync(h => h.SeenAt >= now.AddHours(-1), ct);
        var uniqueMachines = await db.Heartbeats.Select(h => h.MachineId).Distinct().CountAsync(ct);
        var allCount = await db.Heartbeats.CountAsync(ct);

        var products = await db.Heartbeats.AsNoTracking()
            .Join(db.Licenses.AsNoTracking(), h => h.LicenseId, l => l.Id, (h, l) => l.ProductId)
            .Distinct()
            .Join(db.Products.AsNoTracking(), id => id, p => p.Id, (id, p) => new { p.Id, name = p.DisplayName })
            .OrderBy(x => x.name)
            .ToListAsync(ct);

        return Json(200, new
        {
            heartbeats = rows.Select(x => HeartbeatView(x.h, x.l, x.p)),
            total,
            page,
            limit,
            retentionDays = HeartbeatRetentionDays,
            counts = new
            {
                total = allCount,
                last24h,
                lastHour,
                uniqueMachines
            },
            products
        });
    }

    private static object HeartbeatView(Heartbeat h, License l, Product p)
    {
        return new
        {
            h.Id,
            h.LicenseId,
            h.ActivationId,
            h.MachineId,
            h.AppVersion,
            h.OsVersion,
            h.IpAddress,
            seenAt = h.SeenAt.ToString("o"),
            key = l.Key,
            productId = l.ProductId,
            productName = p.DisplayName,
            customerEmail = l.CustomerEmail
        };
    }

    private static object MachineView(Activation a, License l, Product p, DateTime now, TimeSpan stale)
    {
        var last = MachinePresence.EffectiveLastSeen(a.LastSeenAt, a.ActivatedAt);
        var seated = a.DeactivatedAt is null;
        var online = seated && MachinePresence.IsOnline(a.LastSeenAt, a.ActivatedAt, now, stale);
        return new
        {
            a.Id,
            a.LicenseId,
            a.MachineId,
            a.AppVersion,
            a.OsVersion,
            a.IpAddress,
            seated,
            online,
            key = l.Key,
            productId = l.ProductId,
            productName = p.DisplayName,
            customerId = l.CustomerId,
            customerEmail = l.CustomerEmail,
            planType = l.PlanType,
            activatedAt = a.ActivatedAt.ToString("o"),
            lastSeenAt = last.ToString("o"),
            deactivatedAt = a.DeactivatedAt?.ToString("o")
        };
    }

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
