namespace MasterServer.Mail;

public static class MailSenderFactory
{
    public static IMailSender FromEnvironment()
    {
        var provider = (Environment.GetEnvironmentVariable("MAIL_PROVIDER") ?? "console").Trim().ToLowerInvariant();
        var brevoKey = (Environment.GetEnvironmentVariable("BREVO_API_KEY") ?? "").Trim();
        if (provider == BrevoMailSender.ProviderName || (provider is "console" or "" && brevoKey.Length > 0))
        {
            if (brevoKey.Length > 0)
                return new BrevoMailSender(brevoKey);
        }

        var host = Environment.GetEnvironmentVariable("MAIL_SMTP_HOST") ?? "";
        if (provider == SmtpMailSender.ProviderName && host.Length > 0)
        {
            var port = int.TryParse(Environment.GetEnvironmentVariable("MAIL_SMTP_PORT"), out var p) ? p : 587;
            var user = Environment.GetEnvironmentVariable("MAIL_SMTP_USER") ?? "";
            var password = Environment.GetEnvironmentVariable("MAIL_SMTP_PASSWORD") ?? "";
            var startTls = !string.Equals(Environment.GetEnvironmentVariable("MAIL_SMTP_STARTTLS"), "false", StringComparison.OrdinalIgnoreCase);
            return new SmtpMailSender(host, port, user, password, startTls);
        }
        return new ConsoleMailSender();
    }

    public static string DefaultFromEmail() =>
        (Environment.GetEnvironmentVariable("MAIL_FROM") ?? "").Trim();

    public static string DefaultFromName()
    {
        var name = (Environment.GetEnvironmentVariable("MAIL_FROM_NAME") ?? "").Trim();
        return name.Length > 0 ? name : "Support";
    }
}
