using MasterServer.Data;

namespace MasterServer.Admin;

public static class Audit
{
    public static void Record(AppDb db, HttpContext ctx, AdminPrincipal? principal,
        string action, string target = "", string details = "",
        string? username = null, int? userId = null, string? role = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId ?? principal?.User.Id,
            Username = username ?? principal?.User.Username ?? "",
            Role = role ?? principal?.User.Role ?? "",
            Action = action,
            Target = target,
            Details = details,
            IpAddress = ClientIp(ctx),
            CreatedAt = DateTime.UtcNow
        });
    }

    public static string ClientIp(HttpContext ctx)
    {
        var forwarded = ctx.Request.Headers["X-Forwarded-For"].ToString();
        if (forwarded.Length > 0)
        {
            var first = forwarded.Split(',')[0].Trim();
            if (first.Length > 0) return first.Length > 64 ? first[..64] : first;
        }
        var remote = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
        return remote.Length > 64 ? remote[..64] : remote;
    }
}
