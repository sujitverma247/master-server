using System.Text;
using MasterServer.Keys;
using Xunit;

namespace MasterServer.Tests;

public class EvidencePdfTests
{
    private static EvidenceDocument Sample(string? issuer = "Northwind") => new()
    {
        ReportId = "EV-20261016-1",
        GeneratedAtUtc = new DateTime(2026, 10, 16, 2, 0, 0, DateTimeKind.Utc),
        GeneratedBy = "admin",
        IssuerName = issuer ?? "",
        ProductName = "Northwind Guard",
        WebsiteUrl = "https://northwind.example",
        SupportEmail = "help@northwind.example",
        Key = "NW-AAAA-BBBB",
        PlanType = "1year",
        PlanLabel = "1 Year",
        MaxMachines = 3,
        Status = "unused",
        PurchasedAt = new DateTime(2026, 10, 16, 1, 0, 0, DateTimeKind.Utc),
        IssuedAt = new DateTime(2026, 10, 16, 1, 0, 0, DateTimeKind.Utc),
        CustomerName = "Ada",
        CustomerEmail = "ada@example.com"
    };

    [Fact]
    public void Render_is_pdf()
    {
        var pdf = EvidencePdf.Render(Sample());
        Assert.True(pdf.Length > 400);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 8));
        var tail = Encoding.ASCII.GetString(pdf, pdf.Length - 16, 16);
        Assert.Contains("EOF", tail);
    }

    [Fact]
    public void File_name_keeps_key_letters()
    {
        Assert.Equal("evidence_NWAAAABBBB_20261016.pdf", EvidencePdf.FileName("NW-AAAA-BBBB", new DateTime(2026, 10, 16)));
    }

    [Fact]
    public void Copy_and_sample_are_brand_neutral()
    {
        var banned = new[] { "deutsche", "antivirus", "musix", "firewall" };
        var blob = string.Join(" ", EvidencePdf.CopyLines(Sample())).ToLowerInvariant();
        Assert.All(banned, word => Assert.DoesNotContain(word, blob));
        Assert.Contains("northwind", blob);
        Assert.Contains("transaction evidence report", blob);
    }

    [Fact]
    public void Name_is_used_when_logo_is_missing()
    {
        var lines = EvidencePdf.CopyLines(Sample("Acme VPN"));
        Assert.Contains("Acme VPN", lines);
    }
}
