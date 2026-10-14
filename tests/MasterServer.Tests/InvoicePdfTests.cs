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
        PolicyVersion: "2026-10-v1");

    [Fact]
    public void Render_is_pdf_and_contains_invoice_number()
    {
        var pdf = InvoicePdf.Render(Sample());
        Assert.True(pdf.Length > 200);
        var head = Encoding.ASCII.GetString(pdf, 0, 8);
        Assert.StartsWith("%PDF-1.", head);
        var text = Encoding.ASCII.GetString(pdf);
        Assert.Contains("INV-20261015-0001", text);
        Assert.Contains("49.50", text);
        Assert.Contains("%%EOF", text);
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
    public void Labels_and_sample_are_brand_neutral()
    {
        var banned = new[] { "deutsche", "antivirus", "musix", "firewall" };
        var blob = string.Join(" ", InvoicePdf.Lines(Sample())).ToLowerInvariant();
        Assert.All(banned, word => Assert.DoesNotContain(word, blob));
    }

    [Fact]
    public void Escapes_parentheses_in_buyer_name()
    {
        var snap = Sample() with { CustomerName = "Ada (Buyer)" };
        var pdf = InvoicePdf.Render(snap);
        var text = Encoding.ASCII.GetString(pdf);
        Assert.Contains("Ada \\(Buyer\\)", text);
    }
}
