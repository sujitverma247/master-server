using MasterServer.Data;
using MasterServer.Security;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Admin;

/// <summary>
/// Seeds the permission catalog and default role grants on every boot (idempotent), and creates
/// the first admin user from ADMIN_SEED_USERNAME / ADMIN_SEED_PASSWORD when no users exist yet.
/// The seed password is only read from configuration, never stored in source.
/// </summary>
public static class AdminSeed
{
    public static async Task ApplyAsync(AppDb db, IConfiguration config, ILogger logger, CancellationToken ct)
    {
        await SyncPermissionsAsync(db, ct);
        await SyncRoleGrantsAsync(db, ct);
        await SeedFirstAdminAsync(db, config, logger, ct);
    }

    private static async Task SyncPermissionsAsync(AppDb db, CancellationToken ct)
    {
        var existing = await db.Permissions.Select(p => p.Code).ToListAsync(ct);
        var have = existing.ToHashSet(StringComparer.Ordinal);
        foreach (var (code, description) in Permissions.Catalog)
        {
            if (!have.Contains(code))
                db.Permissions.Add(new Permission { Code = code, Description = description });
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SyncRoleGrantsAsync(AppDb db, CancellationToken ct)
    {
        var existing = await db.RolePermissions
            .Select(r => new { r.Role, r.PermissionCode })
            .ToListAsync(ct);
        var have = existing.Select(r => $"{r.Role}\u0000{r.PermissionCode}").ToHashSet(StringComparer.Ordinal);

        foreach (var (role, codes) in Permissions.DefaultRoleGrants)
        {
            foreach (var code in codes)
            {
                if (!have.Contains($"{role}\u0000{code}"))
                    db.RolePermissions.Add(new RolePermission { Role = role, PermissionCode = code });
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedFirstAdminAsync(AppDb db, IConfiguration config, ILogger logger, CancellationToken ct)
    {
        if (await db.AdminUsers.AnyAsync(ct))
            return;

        var username = Read(config, "ADMIN_SEED_USERNAME");
        var password = Read(config, "ADMIN_SEED_PASSWORD");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("No admin seed configured. Set ADMIN_SEED_USERNAME and ADMIN_SEED_PASSWORD to create the first admin.");
            return;
        }

        db.AdminUsers.Add(new AdminUser
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(password),
            Role = Permissions.RoleAdmin,
            DisplayName = Read(config, "ADMIN_SEED_DISPLAY_NAME") is { Length: > 0 } dn ? dn : username,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded first admin user {Username}.", username);
    }

    private static string Read(IConfiguration config, string name)
    {
        var fromEnv = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim();
        return (config[name] ?? "").Trim();
    }
}
