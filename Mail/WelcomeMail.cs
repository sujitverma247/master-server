using System.Net;

namespace MasterServer.Mail;

/// <summary>
/// License-key HTML used by the previous server. Product name, support address, and
/// brand come from vars so this deployment can say Europe Digi Smart Security.
/// </summary>
public static class WelcomeMail
{
    private const string ParaStyle = "margin:0 0 16px";

    public static RenderedMail Build(MailVars vars, string? locale)
    {
        var de = MailLocale.IsGerman(locale);
        var plan = FormatPlan(de, vars.PlanType);
        var display = string.IsNullOrWhiteSpace(vars.CustomerName)
            ? (de ? "" : "Customer")
            : vars.CustomerName.Trim();
        if (de)
        {
            return new(
                $"Willkommen bei {vars.ProductName} - Vielen Dank für Ihr Vertrauen",
                DeHtml(display, vars, plan));
        }
        return new(
            $"Welcome to {vars.ProductName} – Thank You for Choosing Us",
            EnHtml(display, vars, plan));
    }

    public static string FormatPlan(bool german, string? planType)
    {
        var p = (planType ?? "").Trim().ToLowerInvariant();
        return p switch
        {
            "monthly" => german ? "1 Monat" : "1 Month",
            "1year" => german ? "1 Jahr" : "1 Year",
            "2year" => german ? "2 Jahre" : "2 Years",
            "3year" => german ? "3 Jahre" : "3 Years",
            "5year" => german ? "5 Jahre" : "5 Years",
            "lifetime" => german ? "Lebenslang" : "Lifetime",
            _ => ""
        };
    }

    private static string EnHtml(string name, MailVars vars, string plan) =>
        $@"<div style=""font-family:Segoe UI,Arial,sans-serif;max-width:600px;margin:auto;color:#121214;line-height:1.55"">
  <p style=""{ParaStyle}"">Dear {H(name)},</p>
  <p style=""{ParaStyle}"">Thank you for choosing {P(vars.ProductName)}.</p>
  <p style=""{ParaStyle}"">We are pleased to have you as a valued customer and are committed to providing reliable security and protection for your devices.</p>
  <p style=""{ParaStyle}"">Your product activation key is: {K(vars.LicenseKey)}</p>{PlanLine(false, plan)}
  <p style=""{ParaStyle}"">If you require any assistance, our dedicated support team is available Monday through Friday, from 9:00 AM to 6:00 PM. You may contact us at
    {MailTo(vars.SupportEmail)}.</p>
  <p style=""{ParaStyle}"">We also offer a 30-day money-back guarantee. If you wish to request a refund within this period, please contact us at
    {MailTo(vars.SupportEmail)}, and our support team will be happy to assist you.</p>
  <p style=""{ParaStyle}"">We sincerely appreciate your trust in {P(vars.ProductName)} and look forward to providing you with a secure, reliable, and dependable experience.</p>
  <p style=""margin:0"">Kind regards,<br>Support Team<br>{H(vars.BrandName)}</p>
</div>";

    private static string DeHtml(string name, MailVars vars, string plan)
    {
        var greeting = string.IsNullOrWhiteSpace(name)
            ? "Sehr geehrte Damen und Herren"
            : $"Sehr geehrte Damen und Herren {H(name)}";
        return $@"<div style=""font-family:Segoe UI,Arial,sans-serif;max-width:600px;margin:auto;color:#121214;line-height:1.55"">
  <p style=""{ParaStyle}"">{greeting},</p>
  <p style=""{ParaStyle}"">vielen Dank, dass Sie sich für {P(vars.ProductName)} entschieden haben.</p>
  <p style=""{ParaStyle}"">Wir freuen uns, Sie als geschätzten Kunden begrüßen zu dürfen, und sind bestrebt, Ihnen zuverlässige Sicherheit und Schutz für Ihre Geräte zu bieten.</p>
  <p style=""{ParaStyle}"">Ihr Produkt-Aktivierungsschlüssel lautet: {K(vars.LicenseKey)}</p>{PlanLine(true, plan)}
  <p style=""{ParaStyle}"">Sollten Sie Unterstützung benötigen, steht Ihnen unser engagiertes Support-Team Montag bis Freitag von 9:00 Uhr bis 18:00 Uhr gerne zur Verfügung. Sie erreichen uns unter:
    {MailTo(vars.SupportEmail)}.</p>
  <p style=""{ParaStyle}"">Darüber hinaus bieten wir Ihnen eine 30-tägige Geld-zurück-Garantie. Wenn Sie innerhalb dieses Zeitraums eine Rückerstattung wünschen, kontaktieren Sie uns bitte unter
    {MailTo(vars.SupportEmail)}. Unser Support-Team unterstützt Sie gerne bei Ihrem Anliegen.</p>
  <p style=""{ParaStyle}"">Wir bedanken uns herzlich für Ihr Vertrauen in {P(vars.ProductName)} und freuen uns darauf, Ihnen eine sichere, zuverlässige und vertrauenswürdige Nutzung zu ermöglichen.</p>
  <p style=""margin:0"">Mit freundlichen Grüßen<br>Support-Team<br>{H(vars.BrandName)}</p>
</div>";
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string P(string product) => $"<strong>{H(product)}</strong>";
    private static string K(string key) =>
        $"<strong style=\"letter-spacing:0.6px\">{H(key)}</strong>";
    private static string MailTo(string email) =>
        $"<a href=\"mailto:{H(email)}\">{H(email)}</a>";
    private static string PlanLine(bool german, string plan) =>
        string.IsNullOrWhiteSpace(plan)
            ? ""
            : german
                ? $"\n  <p style=\"{ParaStyle}\">Ihr Tarif: {K(plan)}</p>"
                : $"\n  <p style=\"{ParaStyle}\">Your plan: {K(plan)}</p>";
}
