using MasterServer.Data;
using MasterServer.Security;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Admin;

public sealed record AdminPrincipal(AdminUser User, AdminSession Session, HashSet<string> Permissions)
{
    public bool Has(string permission) => Permissions.Contains(permission);
}

/// <summary>
/// Resolves the bearer token on an admin request into a principal with its effective permissions.
/// A missing/expired session, or a disabled user, resolves to null so the caller returns 401.
/// </summary>
public static class AdminAuth
{
    public static async Task<AdminPrincipal?> ResolveAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        var token = header["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
            return null;

        var tokenHash = SessionToken.Hash(token);
        var session = await db.AdminSessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);

        if (session is null || session.ExpiresAt <= DateTime.UtcNow || !session.User.IsActive)
            return null;

        var permissions = await LoadPermissionsAsync(db, session.User.Role, ct);
        return new AdminPrincipal(session.User, session, permissions);
    }

    public static async Task<HashSet<string>> LoadPermissionsAsync(AppDb db, string role, CancellationToken ct)
    {
        var codes = await db.RolePermissions
            .Where(r => r.Role == role)
            .Select(r => r.PermissionCode)
            .ToListAsync(ct);
        return codes.ToHashSet(StringComparer.Ordinal);
    }
}
