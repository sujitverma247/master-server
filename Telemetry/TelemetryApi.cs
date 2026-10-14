using MasterServer.Admin;
using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Telemetry;

public static class TelemetryApi
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static void Map(WebApplication app)
    {
        app.MapGet("/admin/machines", (HttpContext c, AppDb db, CancellationToken ct) => ListMachines(c, db, ct));
        app.MapGet("/admin/heartbeats", (HttpContext c, AppDb db, CancellationToken ct) => ListHeartbeats(c, db, ct));
    }

    private static async Task<IResult> ListMachines(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.TelemetryRead, ct);
        if (g.Result is not null) return g.Result;

        var q = db.Activations.AsNoTracking().AsQueryable();
        var machineId = ctx.Request.Query["machineId"].ToString().Trim();
        if (machineId.Length > 0) q = q.Where(a => a.MachineId == machineId);
        if (int.TryParse(ctx.Request.Query["licenseId"], out var lid)) q = q.Where(a => a.LicenseId == lid);
        if (int.TryParse(ctx.Request.Query["productId"], out var pid))
            q = q.Where(a => db.Licenses.Any(l => l.Id == a.LicenseId && l.ProductId == pid));

        var seatedOnly = ctx.Request.Query["seated"].ToString() == "1";
        if (seatedOnly) q = q.Where(a => a.DeactivatedAt == null);

        var limit = int.TryParse(ctx.Request.Query["limit"], out var lim) ? Math.Clamp(lim, 1, 1000) : 100;
        var rows = await q.OrderByDescending(a => a.LastSeenAt ?? a.ActivatedAt).Take(limit).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var stale = MachinePresence.StaleAfterFromEnvironment();

        Audit.Record(db, ctx, g.Principal, "machine.list", machineId.Length > 0 ? machineId : "all", $"rows={rows.Count}");
        await db.SaveChangesAsync(ct);

        return Json(200, new
        {
            machines = rows.Select(a =>
            {
                var last = MachinePresence.EffectiveLastSeen(a.LastSeenAt, a.ActivatedAt);
                return new
                {
                    a.Id,
                    a.LicenseId,
                    a.MachineId,
                    a.AppVersion,
                    a.OsVersion,
                    a.IpAddress,
                    seated = a.DeactivatedAt is null,
                    online = a.DeactivatedAt is null && MachinePresence.IsOnline(a.LastSeenAt, a.ActivatedAt, now, stale),
                    activatedAt = a.ActivatedAt.ToString("o"),
                    lastSeenAt = last.ToString("o"),
                    deactivatedAt = a.DeactivatedAt?.ToString("o")
                };
            })
        });
    }

    private static async Task<IResult> ListHeartbeats(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.TelemetryRead, ct);
        if (g.Result is not null) return g.Result;

        var q = db.Heartbeats.AsNoTracking().AsQueryable();
        var machineId = ctx.Request.Query["machineId"].ToString().Trim();
        if (machineId.Length > 0) q = q.Where(h => h.MachineId == machineId);
        if (int.TryParse(ctx.Request.Query["licenseId"], out var lid)) q = q.Where(h => h.LicenseId == lid);

        var limit = int.TryParse(ctx.Request.Query["limit"], out var lim) ? Math.Clamp(lim, 1, 1000) : 100;
        var rows = await q.OrderByDescending(h => h.Id).Take(limit).ToListAsync(ct);
        return Json(200, new
        {
            heartbeats = rows.Select(h => new
            {
                h.Id, h.LicenseId, h.ActivationId, h.MachineId, h.AppVersion, h.OsVersion, h.IpAddress,
                seenAt = h.SeenAt.ToString("o")
            })
        });
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
