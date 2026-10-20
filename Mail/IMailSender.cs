namespace MasterServer.Mail;

public sealed record MailSendResult(bool Ok, string Provider, string? MessageId, string? Error);

/// <summary>One delivery backend. Console for local; SMTP or Brevo when configured.</summary>
public interface IMailSender
{
    string Name { get; }
    Task<MailSendResult> SendAsync(string fromEmail, string fromName, string toEmail, string toName,
        string subject, string body, CancellationToken ct);
}
