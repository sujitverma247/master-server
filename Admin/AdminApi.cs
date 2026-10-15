using System.Text.Json;
using MasterServer.Data;
using MasterServer.Security;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Admin;

public static class AdminApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Map(WebApplication app, int sessionHours)
    {
        app.MapPost("/admin/login", (HttpContext ctx, AppDb db, CancellationToken ct) => Login(ctx, db, sessionHours, ct));
        app.MapPost("/admin/logout", (HttpContext ctx, AppDb db, CancellationToken ct) => Logout(ctx, db, ct));
        app.MapGet("/admin/me", (HttpContext ctx, AppDb db, CancellationToken ct) => Me(ctx, db, ct));
        app.MapGet("/admin/users", (HttpContext ctx, AppDb db, CancellationToken ct) => ListUsers(ctx, db, ct));
        app.MapPost("/admin/users", (HttpContext ctx, AppDb db, CancellationToken ct) => CreateUser(ctx, db, ct));
        app.MapPost("/admin/users/update", (HttpContext ctx, AppDb db, CancellationToken ct) => UpdateUser(ctx, db, ct));
        app.MapGet("/admin/audit", (HttpContext ctx, AppDb db, CancellationToken ct) => ListAudit(ctx, db, ct));
    }

    private static async Task<IResult> Login(HttpContext ctx, AppDb db, int sessionHours, CancellationToken ct)
    {
        var body = await ReadBodyAsync(ctx, ct);
        if (body is null)
            return Fail(StatusCodes.Status400BadRequest, "invalid JSON body");

        var username = Str(body.Value, "username");
        var password = Str(body.Value, "password");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return Fail(StatusCodes.Status400BadRequest, "Enter your username and password.");

        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user is null || !user.IsActive || !PasswordHasher.Verify(password, user.PasswordHash))
        {
            Audit.Record(db, ctx, null, "auth.login.failed", username);
            await db.SaveChangesAsync(ct);
            return Fail(StatusCodes.Status401Unauthorized, "Incorrect username or password.");
        }

        var token = SessionToken.New();
        var session = new AdminSession
        {
            TokenHash = SessionToken.Hash(token),
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(sessionHours)
        };
        db.AdminSessions.Add(session);

        var cutoff = DateTime.UtcNow;
        await db.AdminSessions.Where(s => s.UserId == user.Id && s.ExpiresAt < cutoff).ExecuteDeleteAsync(ct);

        user.LastLoginAt = DateTime.UtcNow;
        Audit.Record(db, ctx, null, "auth.login", username);
        await db.SaveChangesAsync(ct);

        var permissions = await AdminAuth.LoadPermissionsAsync(db, user.Role, ct);
        return Json(StatusCodes.Status200OK, new
        {
            token,
            expiresAt = session.ExpiresAt.ToString("o"),
            user = UserView(user, permissions)
        });
    }

    private static async Task<IResult> Logout(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var principal = await AdminAuth.ResolveAsync(ctx, db, ct);
        if (principal is null)
            return Unauthorized();

        db.AdminSessions.Remove(principal.Session);
        Audit.Record(db, ctx, principal, "auth.logout");
        await db.SaveChangesAsync(ct);
        return Json(StatusCodes.Status200OK, new { loggedOut = true });
    }

    private static async Task<IResult> Me(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var principal = await AdminAuth.ResolveAsync(ctx, db, ct);
        if (principal is null)
            return Unauthorized();
        return Json(StatusCodes.Status200OK, new { user = UserView(principal.User, principal.Permissions) });
    }

    private static async Task<IResult> ListUsers(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var principal = await AdminAuth.ResolveAsync(ctx, db, ct);
        if (principal is null)
            return Unauthorized();
        if (!principal.Has(Permissions.UserManage))
            return Forbidden();

        var users = await db.AdminUsers.OrderBy(u => u.Id).ToListAsync(ct);
        return Json(StatusCodes.Status200OK, new
        {
            users = users.Select(u => new
            {
                u.Id,
                u.Username,
                u.Role,
                u.DisplayName,
                u.IsActive,
                createdAt = u.CreatedAt.ToString("o"),
                lastLoginAt = u.LastLoginAt?.ToString("o")
            })
        });
    }

    private static async Task<IResult> CreateUser(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var principal = await AdminAuth.ResolveAsync(ctx, db, ct);
        if (principal is null)
            return Unauthorized();
        if (!principal.Has(Permissions.UserManage))
            return Forbidden();

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null)
            return Fail(StatusCodes.Status400BadRequest, "invalid JSON body");

        var username = Str(body.Value, "username");
        var password = Str(body.Value, "password");
        var role = Str(body.Value, "role");
        var displayName = Str(body.Value, "displayName");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return Fail(StatusCodes.Status400BadRequest, "missing username or password");
        if (string.IsNullOrWhiteSpace(role))
            role = Permissions.RoleAgent;
        if (!await db.RolePermissions.AnyAsync(r => r.Role == role, ct))
            return Fail(StatusCodes.Status422UnprocessableEntity, "unknown role");
        if (await db.AdminUsers.AnyAsync(u => u.Username == username, ct))
            return Fail(StatusCodes.Status409Conflict, "username already exists");

        var user = new AdminUser
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(password),
            Role = role,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.AdminUsers.Add(user);
        Audit.Record(db, ctx, principal, "user.create", username, $"role={role}");
        await db.SaveChangesAsync(ct);

        var permissions = await AdminAuth.LoadPermissionsAsync(db, user.Role, ct);
        return Json(StatusCodes.Status201Created, new { user = UserView(user, permissions) });
    }

    private static async Task<IResult> UpdateUser(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var principal = await AdminAuth.ResolveAsync(ctx, db, ct);
        if (principal is null)
            return Unauthorized();
        if (!principal.Has(Permissions.UserManage))
            return Forbidden();

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null)
            return Fail(StatusCodes.Status400BadRequest, "invalid JSON body");

        if (!TryInt(body.Value, "id", out var id))
            return Fail(StatusCodes.Status400BadRequest, "missing id");

        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return Fail(StatusCodes.Status404NotFound, "user not found");

        var changes = new List<string>();

        var role = Str(body.Value, "role");
        if (!string.IsNullOrWhiteSpace(role) && role != user.Role)
        {
            if (!await db.RolePermissions.AnyAsync(r => r.Role == role, ct))
                return Fail(StatusCodes.Status422UnprocessableEntity, "unknown role");
            user.Role = role;
            changes.Add($"role={role}");
        }

        var displayName = Str(body.Value, "displayName");
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            user.DisplayName = displayName;
            changes.Add("displayName");
        }

        if (TryBool(body.Value, "isActive", out var isActive) && isActive != user.IsActive)
        {
            if (!isActive && user.Id == principal.User.Id)
                return Fail(StatusCodes.Status422UnprocessableEntity, "cannot deactivate yourself");
            user.IsActive = isActive;
            changes.Add($"isActive={isActive}");
            if (!isActive)
                await db.AdminSessions.Where(s => s.UserId == user.Id).ExecuteDeleteAsync(ct);
        }

        var password = Str(body.Value, "password");
        if (!string.IsNullOrWhiteSpace(password))
        {
            user.PasswordHash = PasswordHasher.Hash(password);
            changes.Add("password");
            await db.AdminSessions.Where(s => s.UserId == user.Id && s.UserId != principal.User.Id).ExecuteDeleteAsync(ct);
        }

        Audit.Record(db, ctx, principal, "user.update", user.Username, string.Join(",", changes));
        await db.SaveChangesAsync(ct);

        var permissions = await AdminAuth.LoadPermissionsAsync(db, user.Role, ct);
        return Json(StatusCodes.Status200OK, new { user = UserView(user, permissions) });
    }

    private static async Task<IResult> ListAudit(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var principal = await AdminAuth.ResolveAsync(ctx, db, ct);
        if (principal is null)
            return Unauthorized();
        if (!principal.Has(Permissions.AuditRead))
            return Forbidden();

        var limit = 100;
        if (int.TryParse(ctx.Request.Query["limit"].ToString(), out var parsed))
            limit = Math.Clamp(parsed, 1, 500);

        var rows = await db.AuditLogs
            .OrderByDescending(a => a.Id)
            .Take(limit)
            .ToListAsync(ct);

        return Json(StatusCodes.Status200OK, new
        {
            entries = rows.Select(a => new
            {
                a.Id,
                a.Username,
                a.Role,
                a.Action,
                a.Target,
                a.Details,
                a.IpAddress,
                createdAt = a.CreatedAt.ToString("o")
            })
        });
    }

    private static object UserView(AdminUser user, IEnumerable<string> permissions) => new
    {
        user.Id,
        user.Username,
        user.Role,
        user.DisplayName,
        user.IsActive,
        permissions = permissions.OrderBy(p => p, StringComparer.Ordinal).ToArray()
    };

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

    private static bool TryInt(JsonElement body, string name, out int result)
    {
        result = 0;
        return body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out result);
    }

    private static bool TryBool(JsonElement body, string name, out bool result)
    {
        result = false;
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(name, out var value))
            return false;
        if (value.ValueKind == JsonValueKind.True) { result = true; return true; }
        if (value.ValueKind == JsonValueKind.False) { result = false; return true; }
        return false;
    }

    private static IResult Unauthorized() => Fail(StatusCodes.Status401Unauthorized, "unauthorized");
    private static IResult Forbidden() => Fail(StatusCodes.Status403Forbidden, "forbidden");
    private static IResult Fail(int status, string error) => Json(status, new { error });
    private static IResult Json(int status, object body) => Results.Json(body, JsonOptions, statusCode: status);
}
