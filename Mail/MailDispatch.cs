using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Mail;

public static class MailDispatch
{
    public static async Task<int> DrainAsync(AppDb db, IMailSender sender, int limit, CancellationToken ct)
    {
        var pending = await db.MailOutbox
            .Where(m => m.Status == MailStatuses.Pending || m.Status == MailStatuses.Failed)
            .OrderBy(m => m.Id)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

        var sent = 0;
        foreach (var row in pending)
        {
            if (row.FromEmail.Length == 0)
            {
                row.Status = MailStatuses.Failed;
                row.LastError = "missing from address";
                row.Attempts++;
                continue;
            }

            row.Attempts++;
            var result = await sender.SendAsync(row.FromEmail, row.FromName, row.ToEmail, row.ToName,
                row.Subject, row.Body, ct);
            row.Provider = result.Provider;
            if (result.Ok)
            {
                row.Status = MailStatuses.Sent;
                row.SentAt = DateTime.UtcNow;
                row.ProviderMessageId = result.MessageId;
                row.LastError = "";
                sent++;
            }
            else
            {
                row.Status = MailStatuses.Failed;
                row.LastError = (result.Error ?? "send failed").Length > 512
                    ? (result.Error ?? "")[..512] : (result.Error ?? "send failed");
            }
        }
        if (pending.Count > 0)
            await db.SaveChangesAsync(ct);
        return sent;
    }
}
