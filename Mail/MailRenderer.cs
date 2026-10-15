using System.Text.RegularExpressions;

namespace MasterServer.Mail;

public static class MailRenderer
{
    private static readonly Regex Token = new(@"\{\{([a-zA-Z]+)\}\}", RegexOptions.Compiled);

    public static RenderedMail Render(string templateId, MailVars vars, string? locale = null)
    {
        var template = MailTemplates.Resolve(templateId, locale);
        return new RenderedMail(Replace(template.Subject, vars), Replace(template.Body, vars));
    }

    public static string Replace(string source, MailVars vars)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["productName"] = vars.ProductName,
            ["brandName"] = vars.BrandName,
            ["supportEmail"] = vars.SupportEmail,
            ["customerName"] = vars.CustomerName,
            ["customerEmail"] = vars.CustomerEmail,
            ["licenseKey"] = vars.LicenseKey,
            ["planType"] = vars.PlanType,
            ["orderPublicId"] = vars.OrderPublicId
        };
        return Token.Replace(source, m => map.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }
}

public sealed record RenderedMail(string Subject, string Body);
