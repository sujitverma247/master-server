using System.Text.Json;
using MasterServer.Catalog;
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
        app.MapGet("/admin/users/{id:int}", (int id, HttpContext ctx, AppDb db, CancellationToken ct) => GetUser(id, ctx, db, ct));
        app.MapPost("/admin/users", (HttpContext ctx, AppDb db, CancellationToken ct) => CreateUser(ctx, db, ct));
        app.MapPost("/admin/users/update", (HttpContext ctx, AppDb db, CancellationToken ct) => UpdateUser(ctx, db, ct));
        app.MapGet("/admin/audit", (HttpContext ctx, AppDb db, CancellationToken ct) => ListAudit(ctx, db, ct));
        app.MapGet("/admin/audit/{id:long}", (long id, HttpContext ctx, AppDb db, CancellationToken ct) => GetAudit(id, ctx, db, ct));
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
            Audit.Record(db, ctx, null, "auth.login.failed", username, username: username);
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
        Audit.Record(db, ctx, null, "auth.login", username, username: user.Username, userId: user.Id, role: user.Role);
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

        var q = db.AdminUsers.AsNoTracking().AsQueryable();
        var role = ctx.Request.Query["role"].ToString().Trim();
        if (role.Length > 0)
            q = q.Where(u => u.Role == role);

        var status = ctx.Request.Query["status"].ToString().Trim().ToLowerInvariant();
        if (status == "active") q = q.Where(u => u.IsActive);
        else if (status == "inactive") q = q.Where(u => !u.IsActive);

        var search = ctx.Request.Query["q"].ToString().Trim();
        if (search.Length > 0)
        {
            var s = search.ToLowerInvariant();
            q = q.Where(u =>
                u.Username.ToLower().Contains(s)
                || u.DisplayName.ToLower().Contains(s)
                || u.Role.ToLower().Contains(s));
        }

        if (DateOnly.TryParse(ctx.Request.Query["from"], out var fromDay))
        {
            var fromUtc = fromDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(u => u.CreatedAt >= fromUtc);
        }
        if (DateOnly.TryParse(ctx.Request.Query["to"], out var toDay))
        {
            var toExclusive = toDay.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(u => u.CreatedAt < toExclusive);
        }

        var sort = ctx.Request.Query["sort"].ToString().Trim().ToLowerInvariant();
        var asc = ctx.Request.Query["dir"].ToString().Trim().Equals("asc", StringComparison.OrdinalIgnoreCase);
        q = sort switch
        {
            "id" => asc ? q.OrderBy(u => u.Id) : q.OrderByDescending(u => u.Id),
            "username" => asc ? q.OrderBy(u => u.Username) : q.OrderByDescending(u => u.Username),
            "displayname" or "name" => asc ? q.OrderBy(u => u.DisplayName) : q.OrderByDescending(u => u.DisplayName),
            "role" => asc ? q.OrderBy(u => u.Role) : q.OrderByDescending(u => u.Role),
            "status" => asc ? q.OrderBy(u => u.IsActive) : q.OrderByDescending(u => u.IsActive),
            "lastlogin" or "lastloginat" => asc ? q.OrderBy(u => u.LastLoginAt) : q.OrderByDescending(u => u.LastLoginAt),
            _ => asc ? q.OrderBy(u => u.CreatedAt) : q.OrderByDescending(u => u.CreatedAt)
        };

        var total = await q.CountAsync(ct);
        var limit = CatalogPaging.NormalizeLimit(ctx.Request.Query["limit"].ToString());
        var page = CatalogPaging.ClampPage(CatalogPaging.NormalizePage(ctx.Request.Query["page"].ToString()), total, limit);
        var rows = await q.Skip((page - 1) * limit).Take(limit).ToListAsync(ct);

        var allCount = await db.AdminUsers.CountAsync(ct);
        var activeCount = await db.AdminUsers.CountAsync(u => u.IsActive, ct);
        var adminCount = await db.AdminUsers.CountAsync(u => u.Role == Permissions.RoleAdmin && u.IsActive, ct);
        var roles = await db.RolePermissions.AsNoTracking()
            .Select(r => r.Role)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(ct);
        if (roles.Count == 0)
            roles = [Permissions.RoleAdmin, Permissions.RoleAgent];

        return Json(StatusCodes.Status200OK, new
        {
            users = rows.Select(ListUserView),
            total,
            page,
            limit,
            counts = new
            {
                total = allCount,
                active = activeCount,
                inactive = allCount - activeCount,
                admins = adminCount
            },
            roles
        });
    }

    private static async Task<IResult> GetUser(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var principal = await AdminAuth.ResolveAsync(ctx, db, ct);
        if (principal is null)
            return Unauthorized();
        if (!principal.Has(Permissions.UserManage))
            return Forbidden();

        var user = await db.AdminUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return Fail(StatusCodes.Status404NotFound, "user not found");
        return Json(StatusCodes.Status200OK, new { user = ListUserView(user) });
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
        if (!IsUsername(username))
            return Fail(StatusCodes.Status400BadRequest, "invalid username");
        if (password.Length < 8)
            return Fail(StatusCodes.Status400BadRequest, "password too short");
        if (password.Length > 128)
            return Fail(StatusCodes.Status400BadRequest, "password too long");
        if (displayName.Length > 200)
            return Fail(StatusCodes.Status400BadRequest, "displayName too long");
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
        var nextRole = user.Role;
        if (!string.IsNullOrWhiteSpace(role) && role != user.Role)
        {
            if (!await db.RolePermissions.AnyAsync(r => r.Role == role, ct))
                return Fail(StatusCodes.Status422UnprocessableEntity, "unknown role");
            nextRole = role;
        }

        var nextActive = user.IsActive;
        var hasActive = TryBool(body.Value, "isActive", out var isActive);
        if (hasActive)
            nextActive = isActive;

        if (user.Role == Permissions.RoleAdmin && user.IsActive
            && (nextRole != Permissions.RoleAdmin || !nextActive))
        {
            var otherAdmins = await db.AdminUsers.CountAsync(
                u => u.Id != user.Id && u.Role == Permissions.RoleAdmin && u.IsActive, ct);
            if (otherAdmins == 0)
                return Fail(StatusCodes.Status422UnprocessableEntity, "cannot remove the last admin");
        }

        if (nextRole != user.Role)
        {
            user.Role = nextRole;
            changes.Add($"role={nextRole}");
        }

        var displayName = Str(body.Value, "displayName");
        if (displayName.Length > 200)
            return Fail(StatusCodes.Status400BadRequest, "displayName too long");
        if (!string.IsNullOrWhiteSpace(displayName) && displayName != user.DisplayName)
        {
            user.DisplayName = displayName;
            changes.Add("displayName");
        }

        if (hasActive && nextActive != user.IsActive)
        {
            if (!nextActive && user.Id == principal.User.Id)
                return Fail(StatusCodes.Status422UnprocessableEntity, "cannot deactivate yourself");
            user.IsActive = nextActive;
            changes.Add($"isActive={nextActive}");
            if (!nextActive)
                await db.AdminSessions.Where(s => s.UserId == user.Id).ExecuteDeleteAsync(ct);
        }

        var password = Str(body.Value, "password");
        if (!string.IsNullOrWhiteSpace(password))
        {
            if (password.Length < 8)
                return Fail(StatusCodes.Status400BadRequest, "password too short");
            if (password.Length > 128)
                return Fail(StatusCodes.Status400BadRequest, "password too long");
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

        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        var action = ctx.Request.Query["action"].ToString().Trim();
        if (action.Length > 0)
        {
            if (action.Contains('.'))
                q = q.Where(a => a.Action == action);
            else
            {
                var prefix = action + ".";
                q = q.Where(a => a.Action == action || a.Action.StartsWith(prefix));
            }
        }
        var user = ctx.Request.Query["user"].ToString().Trim();
        if (user.Length > 0)
            q = q.Where(a => a.Username == user || (a.Username == "" && a.Action.StartsWith("auth.") && a.Target == user));

        var search = ctx.Request.Query["q"].ToString().Trim();
        if (search.Length > 0)
        {
            var s = search.ToLowerInvariant();
            q = q.Where(a =>
                a.Username.ToLower().Contains(s)
                || a.Role.ToLower().Contains(s)
                || a.Action.ToLower().Contains(s)
                || a.Target.ToLower().Contains(s)
                || a.Details.ToLower().Contains(s)
                || a.IpAddress.Contains(s));
        }

        if (DateOnly.TryParse(ctx.Request.Query["from"], out var fromDay))
        {
            var fromUtc = fromDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(a => a.CreatedAt >= fromUtc);
        }
        if (DateOnly.TryParse(ctx.Request.Query["to"], out var toDay))
        {
            var toExclusive = toDay.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(a => a.CreatedAt < toExclusive);
        }

        var projected = q.Select(a => new
        {
            a.Id, a.UserId, a.Username, a.Role, a.Action, a.Target, a.Details, a.IpAddress, a.CreatedAt
        });

        var sort = ctx.Request.Query["sort"].ToString().Trim().ToLowerInvariant();
        var asc = ctx.Request.Query["dir"].ToString().Trim().Equals("asc", StringComparison.OrdinalIgnoreCase);
        projected = sort switch
        {
            "id" => asc ? projected.OrderBy(x => x.Id) : projected.OrderByDescending(x => x.Id),
            "user" or "username" => asc ? projected.OrderBy(x => x.Username) : projected.OrderByDescending(x => x.Username),
            "role" => asc ? projected.OrderBy(x => x.Role) : projected.OrderByDescending(x => x.Role),
            "action" => asc ? projected.OrderBy(x => x.Action) : projected.OrderByDescending(x => x.Action),
            "target" => asc ? projected.OrderBy(x => x.Target) : projected.OrderByDescending(x => x.Target),
            "ip" or "ipaddress" => asc ? projected.OrderBy(x => x.IpAddress) : projected.OrderByDescending(x => x.IpAddress),
            _ => asc ? projected.OrderBy(x => x.CreatedAt) : projected.OrderByDescending(x => x.CreatedAt)
        };

        var total = await projected.CountAsync(ct);
        var limit = CatalogPaging.NormalizeLimit(ctx.Request.Query["limit"].ToString());
        var page = CatalogPaging.ClampPage(CatalogPaging.NormalizePage(ctx.Request.Query["page"].ToString()), total, limit);
        var rows = await projected.Skip((page - 1) * limit).Take(limit).ToListAsync(ct);

        var todayStart = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        var allCount = await db.AuditLogs.CountAsync(ct);
        var todayCount = await db.AuditLogs.CountAsync(a => a.CreatedAt >= todayStart, ct);
        var loginCount = await db.AuditLogs.CountAsync(a => a.Action == "auth.login", ct);
        var failedCount = await db.AuditLogs.CountAsync(a => a.Action == "auth.login.failed", ct);
        var actions = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action != "")
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(ct);
        var users = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Username != "")
            .Select(a => a.Username)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(ct);

        return Json(StatusCodes.Status200OK, new
        {
            audit = rows.Select(x => new
            {
                x.Id, x.UserId, x.Username, x.Role, x.Action, x.Target, x.Details, x.IpAddress,
                createdAt = x.CreatedAt.ToString("o")
            }),
            total,
            page,
            limit,
            counts = new { total = allCount, today = todayCount, logins = loginCount, failed = failedCount },
            actions,
            users
        });
    }

    private static async Task<IResult> GetAudit(long id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var principal = await AdminAuth.ResolveAsync(ctx, db, ct);
        if (principal is null)
            return Unauthorized();
        if (!principal.Has(Permissions.AuditRead))
            return Forbidden();

        var row = await db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row is null) return Fail(StatusCodes.Status404NotFound, "audit entry not found");
        return Json(StatusCodes.Status200OK, new
        {
            audit = new
            {
                row.Id, row.UserId, row.Username, row.Role, row.Action, row.Target, row.Details, row.IpAddress,
                createdAt = row.CreatedAt.ToString("o")
            }
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

    private static object ListUserView(AdminUser user) => new
    {
        user.Id,
        user.Username,
        user.Role,
        user.DisplayName,
        user.IsActive,
        createdAt = user.CreatedAt.ToString("o"),
        lastLoginAt = user.LastLoginAt?.ToString("o")
    };

    private static bool IsUsername(string value)
    {
        if (value.Length is < 2 or > 128) return false;
        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') continue;
            return false;
        }
        return true;
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
