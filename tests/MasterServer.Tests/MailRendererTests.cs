using MasterServer.Mail;
using Xunit;

namespace MasterServer.Tests;

public class MailRendererTests
{
    private static MailVars Sample() => new(
        ProductName: "Northwind Guard",
        BrandName: "Northwind",
        SupportEmail: "help@northwind.example",
        CustomerName: "Ada",
        CustomerEmail: "ada@example.com",
        LicenseKey: "ABCD-EFGH",
        PlanType: "1year",
        OrderPublicId: "pub1");

    [Fact]
    public void License_issued_substitutes_vars()
    {
        var mail = MailRenderer.Render(MailTemplates.LicenseIssued, Sample());
        Assert.Contains("Northwind Guard", mail.Subject);
        Assert.Contains("ABCD-EFGH", mail.Body);
        Assert.Contains("help@northwind.example", mail.Body);
        Assert.Contains("Ada", mail.Body);
        Assert.DoesNotContain("{{", mail.Subject);
        Assert.DoesNotContain("{{", mail.Body);
    }

    [Fact]
    public void Refund_includes_order_id()
    {
        var mail = MailRenderer.Render(MailTemplates.OrderRefunded, Sample());
        Assert.Contains("pub1", mail.Body);
        Assert.Contains("no longer valid", mail.Body);
    }

    [Fact]
    public void Unknown_template_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MailRenderer.Render("nope", Sample()));
    }

    [Fact]
    public void German_license_uses_same_layout()
    {
        var mail = MailRenderer.Render(MailTemplates.LicenseIssued, Sample(), "de-DE");
        Assert.Contains("Lizenzschlüssel", mail.Subject);
        Assert.Contains("Hallo Ada", mail.Body);
        Assert.Contains("ABCD-EFGH", mail.Body);
        Assert.Contains("help@northwind.example", mail.Body);
        Assert.DoesNotContain("{{", mail.Subject);
        Assert.DoesNotContain("{{", mail.Body);
        Assert.DoesNotContain("Thank you", mail.Body);
    }

    [Fact]
    public void German_refund_uses_same_layout()
    {
        var mail = MailRenderer.Render(MailTemplates.OrderRefunded, Sample(), "de");
        Assert.Contains("Rückerstattung", mail.Subject);
        Assert.Contains("pub1", mail.Body);
        Assert.Contains("nicht mehr gültig", mail.Body);
        Assert.DoesNotContain("no longer valid", mail.Body);
    }

    [Fact]
    public void Unknown_locale_falls_back_to_english()
    {
        var mail = MailRenderer.Render(MailTemplates.LicenseIssued, Sample(), "fr-FR");
        Assert.Contains("license key", mail.Subject);
        Assert.Contains("Thank you", mail.Body);
    }

    [Fact]
    public void Catalog_templates_are_brand_neutral()
    {
        var banned = new[] { "deutsche", "antivirus", "musix", "firewall" };
        foreach (var template in MailTemplates.All())
        {
            var blob = (template.Subject + " " + template.Body).ToLowerInvariant();
            Assert.All(banned, word => Assert.DoesNotContain(word, blob));
        }
    }

    [Fact]
    public void Known_template_ids()
    {
        Assert.True(MailTemplates.IsKnown(MailTemplates.LicenseIssued));
        Assert.True(MailTemplates.IsKnown(MailTemplates.OrderRefunded));
        Assert.False(MailTemplates.IsKnown("invoice.pdf"));
    }
}
