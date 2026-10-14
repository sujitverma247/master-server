using System.Security.Cryptography;
using System.Text;
using MasterServer.Data;

namespace MasterServer.Invoices;

public sealed record InvoiceSnapshot(
    string Number,
    DateTime IssuedAt,
    string Status,
    string CustomerName,
    string CustomerEmail,
    string BrandName,
    string ProductName,
    string SupportEmail,
    string PlanType,
    decimal Amount,
    string Currency,
    string OrderPublicId,
    string Gateway,
    string PolicyVersion);

/// <summary>
/// Minimal PDF 1.4 writer. No native libraries — Alpine-safe. Labels are brand-neutral;
/// names come from the snapshot (catalog/DB), never from source.
/// </summary>
public static class InvoicePdf
{
    public static InvoiceSnapshot FromEntity(Invoice invoice) => new(
        invoice.Number,
        invoice.IssuedAt,
        invoice.Status,
        invoice.CustomerName,
        invoice.CustomerEmail,
        invoice.BrandName,
        invoice.ProductName,
        invoice.SupportEmail,
        invoice.PlanType,
        invoice.Amount,
        invoice.Currency,
        invoice.OrderPublicId,
        invoice.Gateway,
        invoice.PolicyVersion);

    public static IReadOnlyList<string> Lines(InvoiceSnapshot s) =>
    [
        $"Invoice {s.Number}",
        $"Issued {s.IssuedAt:yyyy-MM-dd}",
        $"Status {s.Status}",
        "",
        "Bill to",
        s.CustomerName,
        s.CustomerEmail,
        "",
        s.BrandName,
        $"{s.ProductName}  {s.PlanType}",
        $"Order {s.OrderPublicId}",
        $"Gateway {s.Gateway}",
        string.IsNullOrEmpty(s.PolicyVersion) ? "Policy (none)" : $"Policy {s.PolicyVersion}",
        "",
        $"Amount {s.Currency} {s.Amount:0.00}",
        "",
        string.IsNullOrEmpty(s.SupportEmail) ? "Questions: support" : $"Questions: {s.SupportEmail}"
    ];

    public static byte[] Render(InvoiceSnapshot snapshot)
    {
        var content = BuildContent(Lines(snapshot));
        return Assemble(content);
    }

    public static string Sha256Hex(byte[] pdf) =>
        Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant();

    private static byte[] BuildContent(IReadOnlyList<string> lines)
    {
        var sb = new StringBuilder();
        sb.Append("BT\n/F1 12 Tf\n72 720 Td\n");
        var first = true;
        foreach (var line in lines)
        {
            if (!first) sb.Append("0 -16 Td\n");
            first = false;
            sb.Append('(').Append(Escape(Sanitize(line))).Append(") Tj\n");
        }
        sb.Append("ET\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static byte[] Assemble(byte[] contentStream)
    {
        var parts = new List<byte[]>();
        void Add(string text) => parts.Add(Encoding.ASCII.GetBytes(text));

        Add("%PDF-1.4\n");
        var offsets = new List<int> { 0 };

        void Obj(int n, string body)
        {
            offsets.Add(parts.Sum(p => p.Length));
            Add($"{n} 0 obj\n{body}\nendobj\n");
        }

        Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Obj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>");

        offsets.Add(parts.Sum(p => p.Length));
        Add($"4 0 obj\n<< /Length {contentStream.Length} >>\nstream\n");
        parts.Add(contentStream);
        Add("\nendstream\nendobj\n");

        Obj(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        var xrefAt = parts.Sum(p => p.Length);
        var xref = new StringBuilder();
        xref.Append("xref\n0 6\n");
        xref.Append("0000000000 65535 f \n");
        for (var i = 1; i <= 5; i++)
            xref.Append($"{offsets[i]:0000000000} 00000 n \n");
        xref.Append($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xrefAt}\n%%EOF\n");
        Add(xref.ToString());

        var total = parts.Sum(p => p.Length);
        var pdf = new byte[total];
        var pos = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, pdf, pos, p.Length);
            pos += p.Length;
        }
        return pdf;
    }

    private static string Sanitize(string text)
    {
        var s = text ?? "";
        var chars = s.Select(c => c <= 127 ? c : '?').ToArray();
        return new string(chars);
    }

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
}
