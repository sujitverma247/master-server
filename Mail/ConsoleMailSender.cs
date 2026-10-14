namespace MasterServer.Mail;

/// <summary>Marks mail sent without talking to a network. Used when SMTP is not configured.</summary>
public sealed class ConsoleMailSender : IMailSender
{
    public const string ProviderName = "console";
    public string Name => ProviderName;

    public Task<MailSendResult> SendAsync(string fromEmail, string fromName, string toEmail, string toName,
        string subject, string body, CancellationToken ct)
    {
        _ = (fromEmail, fromName, toEmail, toName, subject, body);
        return Task.FromResult(new MailSendResult(true, ProviderName, $"console:{Guid.NewGuid():N}", null));
    }
}
