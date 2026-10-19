using System.Text.Json;
using MasterServer.Data;
using MasterServer.Security;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Licensing;

public static class LicenseApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Map(WebApplication app, int skewSeconds)
    {
        app.MapPost("/v1/license/activate", (HttpContext ctx, AppDb db, CancellationToken ct) =>
            Activate(ctx, db, skewSeconds, ct));
        app.MapGet("/v1/license/status", (HttpContext ctx, AppDb db, CancellationToken ct) =>
            Status(ctx, db, skewSeconds, ct));
        app.MapPost("/v1/license/deactivate", (HttpContext ctx, AppDb db, CancellationToken ct) =>
            Deactivate(ctx, db, skewSeconds, ct));
        app.MapPost("/v1/license/heartbeat", (HttpContext ctx, AppDb db, CancellationToken ct) =>
            Heartbeat(ctx, db, skewSeconds, ct));
    }

    public static async Task<IResult> Activate(HttpContext ctx, AppDb db, int skewSeconds, CancellationToken ct)
    {
        var auth = await HmacRequest.AuthenticateAsync(ctx, db, skewSeconds, ct);
        if (auth is not HmacResult.Ok ok)
            return Rejected();

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null)
            return Fail(StatusCodes.Status400BadRequest, "invalid JSON body");

        var key = Str(body.Value, "key");
        var machineId = Str(body.Value, "machineId");
        var appVersion = Str(body.Value, "appVersion");
        var osVersion = Str(body.Value, "osVersion");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(machineId))
            return Fail(StatusCodes.Status400BadRequest, "missing key or machineId");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var license = await LicenseLock.LoadAsync(db, key, ok.Product.Id, ct);
        if (license is null)
            return Fail(StatusCodes.Status404NotFound, "invalid license key");
        if (license.IsRevoked)
            return Fail(StatusCodes.Status422UnprocessableEntity, "license revoked");
        if (!license.IsActive)
            return Fail(StatusCodes.Status422UnprocessableEntity, "license inactive");

        var now = DateTime.UtcNow;
        if (LicenseTerm.IsExpired(license.ExpiresAt, now))
            return Fail(StatusCodes.Status422UnprocessableEntity, "license expired");

        var existing = license.Activations.FirstOrDefault(a => a.MachineId == machineId && a.DeactivatedAt == null);
        if (existing is null)
        {
            var activeCount = license.Activations.Count(a => a.DeactivatedAt == null);
            if (activeCount >= license.MaxMachines)
                return Fail(StatusCodes.Status422UnprocessableEntity, "machine limit reached");
        }

        if (license.ActivatedAt is null)
        {
            license.ActivatedAt = now;
            // Term is owned by purchase / the payment gateway. Never restart it from activation.
            if (license.ExpiresAt is null)
                license.ExpiresAt = await LicenseTerm.FromPurchaseAsync(db, license.PlanType, license.CreatedAt, ct);
        }

        if (existing is null)
        {
            db.Activations.Add(new Activation
            {
                LicenseId = license.Id,
                MachineId = machineId,
                IpAddress = ctx.Connection.RemoteIpAddress?.ToString() ?? "",
                AppVersion = appVersion,
                OsVersion = osVersion,
                ActivatedAt = now,
                LastSeenAt = now
            });
        }
        else
        {
            existing.LastSeenAt = now;
            if (appVersion.Length > 0) existing.AppVersion = appVersion;
            if (osVersion.Length > 0) existing.OsVersion = osVersion;
            existing.IpAddress = ctx.Connection.RemoteIpAddress?.ToString() ?? existing.IpAddress;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var (email, name) = CustomerOf(license);
        return Json(StatusCodes.Status200OK, new
        {
            valid = true,
            isActive = true,
            expiresAt = FormatExpiry(license.ExpiresAt),
            planType = license.PlanType,
            maxMachines = license.MaxMachines,
            email,
            name
        });
    }

    public static async Task<IResult> Status(HttpContext ctx, AppDb db, int skewSeconds, CancellationToken ct)
    {
        var auth = await HmacRequest.AuthenticateAsync(ctx, db, skewSeconds, ct);
        if (auth is not HmacResult.Ok ok)
            return Rejected();

        var key = ctx.Request.Query["key"].ToString();
        if (string.IsNullOrWhiteSpace(key))
            return Fail(StatusCodes.Status400BadRequest, "missing key parameter");

        var license = await db.Licenses.Include(l => l.Customer)
            .FirstOrDefaultAsync(l => l.Key == key && l.ProductId == ok.Product.Id, ct);
        if (license is null)
            return Fail(StatusCodes.Status404NotFound, "invalid license key");

        var now = DateTime.UtcNow;
        var expired = LicenseTerm.IsExpired(license.ExpiresAt, now);
        var valid = !license.IsRevoked && license.IsActive && !expired;
        var (email, name) = CustomerOf(license);

        var machineId = ctx.Request.Query["machineId"].ToString().Trim();
        if (machineId.Length == 0)
        {
            return Json(StatusCodes.Status200OK, new
            {
                valid,
                isActive = license.IsActive,
                isRevoked = license.IsRevoked,
                expired,
                expiresAt = FormatExpiry(license.ExpiresAt),
                planType = license.PlanType,
                email,
                name
            });
        }

        var seat = await db.Activations.AsNoTracking()
            .FirstOrDefaultAsync(a => a.LicenseId == license.Id && a.MachineId == machineId && a.DeactivatedAt == null, ct);
        return Json(StatusCodes.Status200OK, new
        {
            valid,
            isActive = license.IsActive,
            isRevoked = license.IsRevoked,
            expired,
            expiresAt = FormatExpiry(license.ExpiresAt),
            planType = license.PlanType,
            machineId,
            machineSeated = seat is not null,
            lastSeenAt = seat is null ? "" : FormatExpiry(seat.LastSeenAt ?? seat.ActivatedAt),
            email,
            name
        });
    }

    public static async Task<IResult> Deactivate(HttpContext ctx, AppDb db, int skewSeconds, CancellationToken ct)
    {
        var auth = await HmacRequest.AuthenticateAsync(ctx, db, skewSeconds, ct);
        if (auth is not HmacResult.Ok ok)
            return Rejected();

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null)
            return Fail(StatusCodes.Status400BadRequest, "invalid JSON body");

        var key = Str(body.Value, "key");
        var machineId = Str(body.Value, "machineId");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(machineId))
            return Fail(StatusCodes.Status400BadRequest, "missing key or machineId");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var license = await LicenseLock.LoadAsync(db, key, ok.Product.Id, ct);
        if (license is null)
            return Fail(StatusCodes.Status404NotFound, "invalid license key");

        var activation = license.Activations.FirstOrDefault(a => a.MachineId == machineId && a.DeactivatedAt == null);
        if (activation is null)
            return Fail(StatusCodes.Status404NotFound, "no active activation found");

        activation.DeactivatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Json(StatusCodes.Status200OK, new { deactivated = true });
    }

    public static async Task<IResult> Heartbeat(HttpContext ctx, AppDb db, int skewSeconds, CancellationToken ct)
    {
        var auth = await HmacRequest.AuthenticateAsync(ctx, db, skewSeconds, ct);
        if (auth is not HmacResult.Ok ok)
            return Rejected();

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null)
            return Fail(StatusCodes.Status400BadRequest, "invalid JSON body");

        var key = Str(body.Value, "key");
        var machineId = Str(body.Value, "machineId");
        var appVersion = Str(body.Value, "appVersion");
        var osVersion = Str(body.Value, "osVersion");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(machineId))
            return Fail(StatusCodes.Status400BadRequest, "missing key or machineId");

        var license = await db.Licenses.Include(l => l.Customer)
            .FirstOrDefaultAsync(l => l.Key == key && l.ProductId == ok.Product.Id, ct);
        if (license is null)
            return Fail(StatusCodes.Status404NotFound, "invalid license key");

        var now = DateTime.UtcNow;
        var expired = LicenseTerm.IsExpired(license.ExpiresAt, now);
        if (license.IsRevoked)
            return Fail(StatusCodes.Status422UnprocessableEntity, "license revoked");
        if (!license.IsActive)
            return Fail(StatusCodes.Status422UnprocessableEntity, "license inactive");
        if (expired)
            return Fail(StatusCodes.Status422UnprocessableEntity, "license expired");

        var seat = await db.Activations.FirstOrDefaultAsync(
            a => a.LicenseId == license.Id && a.MachineId == machineId && a.DeactivatedAt == null, ct);
        if (seat is null)
            return Fail(StatusCodes.Status422UnprocessableEntity, "machine not activated");

        seat.LastSeenAt = now;
        if (appVersion.Length > 0) seat.AppVersion = appVersion;
        if (osVersion.Length > 0) seat.OsVersion = osVersion;
        seat.IpAddress = ctx.Connection.RemoteIpAddress?.ToString() ?? seat.IpAddress;

        db.Heartbeats.Add(new Heartbeat
        {
            LicenseId = license.Id,
            ActivationId = seat.Id,
            MachineId = machineId,
            AppVersion = seat.AppVersion,
            OsVersion = seat.OsVersion,
            IpAddress = seat.IpAddress,
            SeenAt = now
        });

        var cutoff = now.AddDays(-14);
        await db.Heartbeats.Where(h => h.SeenAt < cutoff).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);

        var (email, name) = CustomerOf(license);
        return Json(StatusCodes.Status200OK, new
        {
            valid = true,
            isActive = true,
            isRevoked = false,
            expired = false,
            expiresAt = FormatExpiry(license.ExpiresAt),
            planType = license.PlanType,
            machineId,
            lastSeenAt = FormatExpiry(now),
            email,
            name
        });
    }

    public static int PlanDurationDays(string planType) => PlanCatalog.DefaultDurationDays(planType);

    private static string FormatExpiry(DateTime? expiresAt) =>
        expiresAt is DateTime value ? value.ToString("o") : "";

    private static (string email, string name) CustomerOf(License license)
    {
        var email = license.Customer?.Email;
        if (string.IsNullOrWhiteSpace(email))
            email = license.CustomerEmail;
        var name = license.Customer?.DisplayName ?? "";
        return (email ?? "", name);
    }

    private static async Task<JsonElement?> ReadBodyAsync(HttpContext ctx, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ct);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Str(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object
        && body.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? "").Trim()
            : "";

    private static IResult Rejected() => Fail(StatusCodes.Status401Unauthorized, "unauthorized");

    private static IResult Fail(int status, string error) => Json(status, new { error });

    private static IResult Json(int status, object body) =>
        Results.Json(body, JsonOptions, statusCode: status);
}
