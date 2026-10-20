using System.Net;

namespace MasterServer.Mail;

/// <summary>Refund notice in the same HTML chrome as the license welcome mail.</summary>
public static class RefundMail
{
    private const string ParaStyle = "margin:0 0 16px";

    public static RenderedMail Build(MailVars vars, string? locale)
    {
        var de = MailLocale.IsGerman(locale);
        var display = string.IsNullOrWhiteSpace(vars.CustomerName)
            ? (de ? "" : "Customer")
            : vars.CustomerName.Trim();
        if (de)
        {
            return new(
                $"Rückerstattung für {vars.ProductName} ausgeführt",
                DeHtml(display, vars));
        }
        return new(
            $"Refund processed for {vars.ProductName}",
            EnHtml(display, vars));
    }

    private static string EnHtml(string name, MailVars vars) =>
        $@"<div style=""font-family:Segoe UI,Arial,sans-serif;max-width:600px;margin:auto;color:#121214;line-height:1.55"">
  <p style=""{ParaStyle}"">Dear {H(name)},</p>
  <p style=""{ParaStyle}"">A refund was processed for your {P(vars.ProductName)} order {H(vars.OrderPublicId)}.</p>
  <p style=""{ParaStyle}"">The associated license key is no longer valid.</p>
  <p style=""{ParaStyle}"">If you have questions, contact {MailTo(vars.SupportEmail)}.</p>
  <p style=""margin:0"">Kind regards,<br>Support Team<br>{H(vars.BrandName)}</p>
</div>";

    private static string DeHtml(string name, MailVars vars)
    {
        var greeting = string.IsNullOrWhiteSpace(name)
            ? "Sehr geehrte Damen und Herren"
            : $"Sehr geehrte Damen und Herren {H(name)}";
        return $@"<div style=""font-family:Segoe UI,Arial,sans-serif;max-width:600px;margin:auto;color:#121214;line-height:1.55"">
  <p style=""{ParaStyle}"">{greeting},</p>
  <p style=""{ParaStyle}"">für Ihre Bestellung {H(vars.OrderPublicId)} von {P(vars.ProductName)} wurde eine Rückerstattung ausgeführt.</p>
  <p style=""{ParaStyle}"">Der zugehörige Lizenzschlüssel ist nicht mehr gültig.</p>
  <p style=""{ParaStyle}"">Bei Fragen wenden Sie sich an {MailTo(vars.SupportEmail)}.</p>
  <p style=""margin:0"">Mit freundlichen Grüßen<br>Support-Team<br>{H(vars.BrandName)}</p>
</div>";
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string P(string product) => $"<strong>{H(product)}</strong>";
    private static string MailTo(string email) =>
        $"<a href=\"mailto:{H(email)}\">{H(email)}</a>";
}
