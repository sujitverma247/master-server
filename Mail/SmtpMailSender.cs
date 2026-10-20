using System.Net;
using System.Net.Mail;

namespace MasterServer.Mail;

public sealed class SmtpMailSender : IMailSender
{
    public const string ProviderName = "smtp";

    private readonly string _host;
    private readonly int _port;
    private readonly string _user;
    private readonly string _password;
    private readonly bool _startTls;

    public SmtpMailSender(string host, int port, string user, string password, bool startTls)
    {
        _host = host;
        _port = port;
        _user = user;
        _password = password;
        _startTls = startTls;
    }

    public string Name => ProviderName;

    public async Task<MailSendResult> SendAsync(string fromEmail, string fromName, string toEmail, string toName,
        string subject, string body, CancellationToken ct)
    {
        try
        {
            using var client = new SmtpClient(_host, _port)
            {
                EnableSsl = _startTls,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };
            if (_user.Length > 0)
                client.Credentials = new NetworkCredential(_user, _password);

            using var msg = new MailMessage
            {
                From = new System.Net.Mail.MailAddress(fromEmail, fromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            msg.To.Add(new System.Net.Mail.MailAddress(toEmail, toName));
            await client.SendMailAsync(msg, ct);
            return new MailSendResult(true, ProviderName, null, null);
        }
        catch (Exception ex)
        {
            return new MailSendResult(false, ProviderName, null, ex.Message);
        }
    }
}
