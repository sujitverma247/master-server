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
        if (license.ActivatedAt is not null && license.ExpiresAt is DateTime already && already < now)
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
            license.ExpiresAt ??= now.AddDays(PlanDurationDays(license.PlanType));
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
                ActivatedAt = now
            });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Json(StatusCodes.Status200OK, new
        {
            valid = true,
            isActive = true,
            expiresAt = FormatExpiry(license.ExpiresAt),
            planType = license.PlanType,
            maxMachines = license.MaxMachines
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

        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Key == key && l.ProductId == ok.Product.Id, ct);
        if (license is null)
            return Fail(StatusCodes.Status404NotFound, "invalid license key");

        var now = DateTime.UtcNow;
        var expired = license.ActivatedAt is not null && license.ExpiresAt is DateTime exp && exp < now;
        return Json(StatusCodes.Status200OK, new
        {
            valid = !license.IsRevoked && license.IsActive && !expired,
            isActive = license.IsActive,
            isRevoked = license.IsRevoked,
            expired,
            expiresAt = FormatExpiry(license.ExpiresAt),
            planType = license.PlanType
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

    public static int PlanDurationDays(string planType) => planType.Trim().ToLowerInvariant() switch
    {
        "monthly" => 30,
        "1year" => 365,
        "2year" => 730,
        "3year" => 1095,
        "5year" => 1825,
        "lifetime" => 36500,
        _ => 365
    };

    private static string FormatExpiry(DateTime? expiresAt) =>
        expiresAt is DateTime value ? value.ToString("o") : "";

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
