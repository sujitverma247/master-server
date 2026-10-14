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
    public void Catalog_templates_are_brand_neutral()
    {
        var banned = new[] { "deutsche", "antivirus", "musix", "firewall" };
        foreach (var template in MailTemplates.Catalog.Values)
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
