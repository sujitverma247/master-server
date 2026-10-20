namespace MasterServer.Mail;

/// <summary>Brand-neutral template ids. Names/support addresses come from vars, not source.</summary>
public static class MailTemplates
{
    public const string LicenseIssued = "license.issued";
    public const string OrderRefunded = "order.refunded";

    public static bool IsKnown(string template) =>
        template is LicenseIssued or OrderRefunded;

    public static IEnumerable<MailTemplate> All()
    {
        var tokens = new MailVars(
            ProductName: "{{productName}}",
            BrandName: "{{brandName}}",
            SupportEmail: "{{supportEmail}}",
            CustomerName: "{{customerName}}",
            CustomerEmail: "{{customerEmail}}",
            LicenseKey: "{{licenseKey}}",
            PlanType: "",
            OrderPublicId: "{{orderPublicId}}");
        yield return ToTemplate(WelcomeMail.Build(tokens, MailLocale.English));
        yield return ToTemplate(WelcomeMail.Build(tokens, MailLocale.German));
        yield return ToTemplate(RefundMail.Build(tokens, MailLocale.English));
        yield return ToTemplate(RefundMail.Build(tokens, MailLocale.German));
    }

    private static MailTemplate ToTemplate(RenderedMail mail) => new(mail.Subject, mail.Body);
}

public sealed record MailTemplate(string Subject, string Body);

public sealed record MailVars(
    string ProductName,
    string BrandName,
    string SupportEmail,
    string CustomerName,
    string CustomerEmail,
    string LicenseKey,
    string PlanType,
    string OrderPublicId);
