namespace MasterServer.Data;

/// <summary>Outbound mail outbox. Bodies may contain a license key; audit rows must not.</summary>
public class OutboundMail
{
    public long Id { get; set; }
    public string Template { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string ToEmail { get; set; } = "";
    public string ToName { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string FromName { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string Status { get; set; } = MailStatuses.Pending;
    public string Provider { get; set; } = "";
    public string? ProviderMessageId { get; set; }
    public int Attempts { get; set; }
    public string LastError { get; set; } = "";
    public int? ProductId { get; set; }
    public int? CustomerId { get; set; }
    public int? OrderId { get; set; }
    public int? LicenseId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
}

public static class MailStatuses
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Failed = "failed";
}
