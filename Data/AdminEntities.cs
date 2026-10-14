namespace MasterServer.Data;

/// <summary>A panel operator. Passwords are PBKDF2-hashed (iterations.saltB64.hashB64).</summary>
public class AdminUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "agent";
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>A server-side bearer session. Stored as a hash so a DB leak cannot replay tokens.</summary>
public class AdminSession
{
    public long Id { get; set; }
    public string TokenHash { get; set; } = "";
    public int UserId { get; set; }
    public AdminUser User { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}

/// <summary>A granular permission code (e.g. "user.manage"). Seeded from the code catalog.</summary>
public class Permission
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>Maps a role to a permission code. A user's effective permissions are the union for its role.</summary>
public class RolePermission
{
    public int Id { get; set; }
    public string Role { get; set; } = "";
    public string PermissionCode { get; set; } = "";
}

/// <summary>Immutable "who did what". Stores key ids / emails / subscription ids, never full secrets.</summary>
public class AuditLog
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Details { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
