using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MasterServer.Mail;

/// <summary>Same HTTPS path the previous license server used (Brevo transactional API).</summary>
public sealed class BrevoMailSender : IMailSender
{
    public const string ProviderName = "brevo";
    private static readonly Uri SendUri = new("https://api.brevo.com/v3/smtp/email");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly string _apiKey;

    public BrevoMailSender(string apiKey)
    {
        _apiKey = apiKey;
    }

    public string Name => ProviderName;

    public async Task<MailSendResult> SendAsync(string fromEmail, string fromName, string toEmail, string toName,
        string subject, string body, CancellationToken ct)
    {
        try
        {
            await SendOnceAsync(fromEmail, fromName, toEmail, toName, subject, body, ct);
            return new MailSendResult(true, ProviderName, null, null);
        }
        catch (BrevoMailException ex) when (ex.IsTransient)
        {
            try
            {
                await Task.Delay(800, ct);
                await SendOnceAsync(fromEmail, fromName, toEmail, toName, subject, body, ct);
                return new MailSendResult(true, ProviderName, null, null);
            }
            catch (Exception retry)
            {
                return Fail(retry);
            }
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    private async Task SendOnceAsync(string fromEmail, string fromName, string toEmail, string toName,
        string subject, string body, CancellationToken ct)
    {
        var sender = new Dictionary<string, object> { ["email"] = fromEmail };
        if (fromName.Length > 0) sender["name"] = fromName;

        var to = new Dictionary<string, object> { ["email"] = toEmail };
        if (toName.Length > 0) to["name"] = toName;

        var payload = new Dictionary<string, object?>
        {
            ["sender"] = sender,
            ["to"] = new[] { to },
            ["subject"] = subject,
            ["htmlContent"] = body
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, SendUri);
        req.Headers.TryAddWithoutValidation("api-key", _apiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new BrevoMailException((int)resp.StatusCode, text);
    }

    private static MailSendResult Fail(Exception ex)
    {
        var message = ex.Message;
        if (message.Length > 512) message = message[..512];
        return new MailSendResult(false, ProviderName, null, message);
    }
}

internal sealed class BrevoMailException : InvalidOperationException
{
    public int StatusCode { get; }
    public bool IsQuota => StatusCode == 402;
    public bool IsTransient => StatusCode is 408 or 429 || StatusCode >= 500;

    public BrevoMailException(int statusCode, string body)
        : base($"Brevo {statusCode}: {body}")
    {
        StatusCode = statusCode;
    }
}
