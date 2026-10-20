using MasterServer.Mail;
using Xunit;

namespace MasterServer.Tests;

public class MailRendererTests
{
    private static MailVars Sample() => new(
        ProductName: "Europe Digi Smart Security",
        BrandName: "Europe Digi Systems",
        SupportEmail: "contact@europedigisystems.com",
        CustomerName: "Ada",
        CustomerEmail: "ada@example.com",
        LicenseKey: "ABCD-EFGH",
        PlanType: "1year",
        OrderPublicId: "pub1");

    [Fact]
    public void License_issued_matches_previous_server_layout()
    {
        var mail = MailRenderer.Render(MailTemplates.LicenseIssued, Sample());
        Assert.Equal("Welcome to Europe Digi Smart Security – Thanks for Your Trust", mail.Subject);
        Assert.Contains("Dear Ada,", mail.Body);
        Assert.Contains("<strong>Europe Digi Smart Security</strong>", mail.Body);
        Assert.Contains("Your activation key:", mail.Body);
        Assert.Contains("ABCD-EFGH", mail.Body);
        Assert.Contains("Your plan:", mail.Body);
        Assert.Contains("1 Year", mail.Body);
        Assert.Contains("mailto:contact@europedigisystems.com", mail.Body);
        Assert.Contains("contact@europedigisystems.com", mail.Body);
        Assert.Contains("30-day money-back guarantee", mail.Body);
        Assert.Contains("Monday through Friday, 9:00 AM to 6:00 PM", mail.Body);
        Assert.Contains("Support Team", mail.Body);
        Assert.Contains("Europe Digi Systems", mail.Body);
        Assert.DoesNotContain("Firewall", mail.Body);
        Assert.DoesNotContain("deutschesoftwares.com", mail.Body);
        Assert.DoesNotContain("{{", mail.Subject);
        Assert.DoesNotContain("{{", mail.Body);
    }

    [Fact]
    public void Refund_includes_order_id()
    {
        var mail = MailRenderer.Render(MailTemplates.OrderRefunded, Sample());
        Assert.Contains("pub1", mail.Body);
        Assert.Contains("no longer valid", mail.Body);
        Assert.Contains("mailto:contact@europedigisystems.com", mail.Body);
        Assert.Contains("<div", mail.Body);
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
        Assert.Contains("Willkommen bei Europe Digi Smart Security", mail.Subject);
        Assert.Contains("Sehr geehrte Damen und Herren Ada", mail.Body);
        Assert.Contains("Aktivierungsschlüssel", mail.Body);
        Assert.Contains("ABCD-EFGH", mail.Body);
        Assert.Contains("1 Jahr", mail.Body);
        Assert.Contains("mailto:contact@europedigisystems.com", mail.Body);
        Assert.DoesNotContain("{{", mail.Subject);
        Assert.DoesNotContain("{{", mail.Body);
        Assert.DoesNotContain("Thank you for choosing", mail.Body);
        Assert.DoesNotContain("Deutsche Softwares", mail.Body);
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
        Assert.Contains("Thanks for Your Trust", mail.Subject);
        Assert.Contains("Dear Ada,", mail.Body);
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

    [Fact]
    public void Html_encodes_customer_name()
    {
        var vars = Sample() with { CustomerName = "<script>x</script>" };
        var mail = MailRenderer.Render(MailTemplates.LicenseIssued, vars);
        Assert.DoesNotContain("<script>", mail.Body);
        Assert.Contains("&lt;script&gt;x&lt;/script&gt;", mail.Body);
    }

    [Fact]
    public void Empty_name_uses_customer_in_english()
    {
        var vars = Sample() with { CustomerName = "" };
        var mail = MailRenderer.Render(MailTemplates.LicenseIssued, vars);
        Assert.Contains("Dear Customer,", mail.Body);
    }
}
