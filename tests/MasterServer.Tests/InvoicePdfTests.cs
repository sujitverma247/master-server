using System.Text;
using MasterServer.Invoices;
using Xunit;

namespace MasterServer.Tests;

public class InvoicePdfTests
{
    private static InvoiceSnapshot Sample() => new(
        Number: "INV-20261015-0001",
        IssuedAt: new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc),
        Status: "issued",
        CustomerName: "Ada",
        CustomerEmail: "ada@example.com",
        BrandName: "Northwind",
        ProductName: "Northwind Guard",
        SupportEmail: "help@northwind.example",
        PlanType: "1year",
        Amount: 49.50m,
        Currency: "EUR",
        OrderPublicId: "abc123",
        Gateway: "dev",
        PolicyVersion: "2026-10-v1",
        WebsiteUrl: "https://northwind.example",
        CustomerPhone: "+44 7700 900123",
        PlanLabel: "1 Year",
        PaidAt: new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Render_is_pdf()
    {
        var pdf = InvoicePdf.Render(Sample());
        Assert.True(pdf.Length > 400);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 8));
        var tail = Encoding.ASCII.GetString(pdf, pdf.Length - 16, 16);
        Assert.Contains("EOF", tail);
    }

    [Fact]
    public void Same_snapshot_is_byte_stable()
    {
        var a = InvoicePdf.Render(Sample());
        var b = InvoicePdf.Render(Sample());
        Assert.Equal(a, b);
        Assert.Equal(InvoicePdf.Sha256Hex(a), InvoicePdf.Sha256Hex(b));
        Assert.Equal(64, InvoicePdf.Sha256Hex(a).Length);
    }

    [Fact]
    public void Copy_is_brand_neutral_and_includes_standard_fields()
    {
        var banned = new[] { "deutsche", "antivirus", "musix", "firewall" };
        var blob = string.Join(" ", InvoicePdf.Lines(Sample())).ToLowerInvariant();
        Assert.All(banned, word => Assert.DoesNotContain(word, blob));
        Assert.Contains("inv-20261015-0001", blob);
        Assert.Contains("northwind", blob);
        Assert.Contains("ada@example.com", blob);
        Assert.Contains("€49.50", blob);
        Assert.Contains("bill to", blob);
        Assert.Contains("paid", blob);
    }

    [Fact]
    public void Void_status_is_labelled_cancelled()
    {
        Assert.Equal("Cancelled", InvoicePdf.StatusLabel("void"));
        Assert.Equal("Paid", InvoicePdf.StatusLabel("issued"));
        var lines = InvoicePdf.Lines(Sample() with { Status = "void" });
        Assert.Contains("Cancelled", lines);
        Assert.Contains("This invoice is cancelled and is not a claim for payment.", lines);
    }

    [Fact]
    public void Buyer_name_with_parentheses_renders()
    {
        var pdf = InvoicePdf.Render(Sample() with { CustomerName = "Ada (Buyer)" });
        Assert.True(pdf.Length > 400);
        Assert.Contains("Ada (Buyer)", InvoicePdf.Lines(Sample() with { CustomerName = "Ada (Buyer)" }));
    }
}
