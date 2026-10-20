using System.Net;
using System.Text.RegularExpressions;

namespace MasterServer.Mail;

public static class MailRenderer
{
    private static readonly Regex Token = new(@"\{\{([a-zA-Z]+)\}\}", RegexOptions.Compiled);

    public static RenderedMail Render(string templateId, MailVars vars, string? locale = null)
    {
        if (!MailTemplates.IsKnown(templateId))
            throw new ArgumentOutOfRangeException(nameof(templateId), "unknown mail template");
        return templateId == MailTemplates.LicenseIssued
            ? WelcomeMail.Build(vars, locale)
            : RefundMail.Build(vars, locale);
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
        return Token.Replace(source, m => map.TryGetValue(m.Groups[1].Value, out var v)
            ? WebUtility.HtmlEncode(v)
            : m.Value);
    }
}

public sealed record RenderedMail(string Subject, string Body);
