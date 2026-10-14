using MasterServer.Data;

namespace MasterServer.Admin;

public static class Audit
{
    public static void Record(AppDb db, HttpContext ctx, AdminPrincipal? principal,
        string action, string target = "", string details = "")
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = principal?.User.Id,
            Username = principal?.User.Username ?? "",
            Role = principal?.User.Role ?? "",
            Action = action,
            Target = target,
            Details = details,
            IpAddress = ctx.Connection.RemoteIpAddress?.ToString() ?? "",
            CreatedAt = DateTime.UtcNow
        });
    }
}
