using System.Net;

namespace MasterServer.Mail;

/// <summary>
/// License-key welcome mail. Same facts and layout as the previous server;
/// wording is our own. Product, support, and brand come from vars.
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
                $"Willkommen bei {vars.ProductName} – Danke für Ihr Vertrauen",
                DeHtml(display, vars, plan));
        }
        return new(
            $"Welcome to {vars.ProductName} – Thanks for Your Trust",
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
  <p style=""{ParaStyle}"">Thank you for purchasing {P(vars.ProductName)}.</p>
  <p style=""{ParaStyle}"">We are glad to have you with us and will keep your devices protected with reliable security.</p>
  <p style=""{ParaStyle}"">Your activation key: {K(vars.LicenseKey)}</p>{PlanLine(false, plan)}
  <p style=""{ParaStyle}"">If you need help, our support team is available Monday through Friday, 9:00 AM to 6:00 PM. You can reach us at
    {MailTo(vars.SupportEmail)}.</p>
  <p style=""{ParaStyle}"">You also have a 30-day money-back guarantee. To request a refund in that period, write to
    {MailTo(vars.SupportEmail)} and we will assist you.</p>
  <p style=""{ParaStyle}"">Thank you for trusting {P(vars.ProductName)}. We look forward to a secure and dependable experience for you.</p>
  <p style=""margin:0"">Kind regards,<br>Support Team<br>{H(vars.BrandName)}</p>
</div>";

    private static string DeHtml(string name, MailVars vars, string plan)
    {
        var greeting = string.IsNullOrWhiteSpace(name)
            ? "Sehr geehrte Damen und Herren"
            : $"Sehr geehrte Damen und Herren {H(name)}";
        return $@"<div style=""font-family:Segoe UI,Arial,sans-serif;max-width:600px;margin:auto;color:#121214;line-height:1.55"">
  <p style=""{ParaStyle}"">{greeting},</p>
  <p style=""{ParaStyle}"">vielen Dank für Ihren Kauf von {P(vars.ProductName)}.</p>
  <p style=""{ParaStyle}"">Wir begrüßen Sie gerne und sorgen für zuverlässigen Schutz Ihrer Geräte.</p>
  <p style=""{ParaStyle}"">Ihr Aktivierungsschlüssel: {K(vars.LicenseKey)}</p>{PlanLine(true, plan)}
  <p style=""{ParaStyle}"">Bei Fragen erreichen Sie unser Support-Team montags bis freitags von 9:00 bis 18:00 Uhr unter
    {MailTo(vars.SupportEmail)}.</p>
  <p style=""{ParaStyle}"">Sie haben 30 Tage Geld-zurück-Garantie. Für eine Rückerstattung in diesem Zeitraum schreiben Sie uns unter
    {MailTo(vars.SupportEmail)} – wir helfen Ihnen gerne.</p>
  <p style=""{ParaStyle}"">Danke für Ihr Vertrauen in {P(vars.ProductName)}. Wir freuen uns, Ihnen eine sichere und zuverlässige Nutzung zu bieten.</p>
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
