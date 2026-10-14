namespace MasterServer.Data;

/// <summary>
/// A snapshot invoice for a paid order. Amount and buyer/product names are copied at issue time
/// so later catalog edits do not rewrite history. PDF bytes are stored; the hash is the evidence id.
/// </summary>
public class Invoice
{
    public int Id { get; set; }
    public string PublicId { get; set; } = "";
    public string Number { get; set; } = "";
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int CustomerId { get; set; }
    public string CustomerEmail { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string BrandName { get; set; } = "";
    public string SupportEmail { get; set; } = "";
    public string PlanType { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Gateway { get; set; } = "";
    public string OrderPublicId { get; set; } = "";
    public string PolicyVersion { get; set; } = "";
    public string Status { get; set; } = InvoiceStatuses.Issued;
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? VoidedAt { get; set; }
    public string PdfSha256 { get; set; } = "";
    public byte[] PdfBytes { get; set; } = Array.Empty<byte>();
}

public static class InvoiceStatuses
{
    public const string Issued = "issued";
    public const string Void = "void";
}
