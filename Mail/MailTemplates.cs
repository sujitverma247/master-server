namespace MasterServer.Mail;

/// <summary>Brand-neutral template ids and bodies. Names/support addresses come from vars, not source.</summary>
public static class MailTemplates
{
    public const string LicenseIssued = "license.issued";
    public const string OrderRefunded = "order.refunded";

    public static readonly IReadOnlyDictionary<string, MailTemplate> Catalog = new Dictionary<string, MailTemplate>
    {
        [LicenseIssued] = new(
            "Your {{productName}} license key",
            """
            Hello {{customerName}},

            Thank you for your purchase of {{productName}} ({{planType}}).

            Your license key:
            {{licenseKey}}

            Keep this email. You will need the key to activate the product.
            If you have questions, contact {{supportEmail}}.

            {{brandName}}
            """),
        [OrderRefunded] = new(
            "Refund processed for {{productName}}",
            """
            Hello {{customerName}},

            A refund was processed for your {{productName}} order {{orderPublicId}}.
            The associated license key is no longer valid.

            If you have questions, contact {{supportEmail}}.

            {{brandName}}
            """)
    };

    public static bool IsKnown(string template) => Catalog.ContainsKey(template);
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
