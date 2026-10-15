using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MasterServer.Keys;

public sealed class EvidenceDocument
{
    public string ReportId { get; init; } = "";
    public DateTime GeneratedAtUtc { get; init; }
    public string GeneratedBy { get; init; } = "";
    public string IssuerName { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string WebsiteUrl { get; init; } = "";
    public string SupportEmail { get; init; } = "";
    public byte[]? LogoBytes { get; init; }

    public string Key { get; init; } = "";
    public string PlanType { get; init; } = "";
    public string PlanLabel { get; init; } = "";
    public int MaxMachines { get; init; }
    public string Status { get; init; } = "";
    public DateTime PurchasedAt { get; init; }
    public DateTime IssuedAt { get; init; }
    public DateTime? ActivatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string Source { get; init; } = "";
    public string? ExternalPaymentId { get; init; }
    public string? ExternalSubscriptionId { get; init; }

    public string CustomerName { get; init; } = "";
    public string CustomerEmail { get; init; } = "";

    public string? OrderPublicId { get; init; }
    public string? OrderStatus { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public string? Gateway { get; init; }
    public DateTime? PaidAt { get; init; }
    public string? InvoiceNumber { get; init; }
    public string? InvoiceStatus { get; init; }
    public string? InvoiceSha { get; init; }

    public IReadOnlyList<EvidenceMailRow> Mail { get; init; } = [];
    public IReadOnlyList<EvidenceActivationRow> Activations { get; init; } = [];
    public IReadOnlyList<EvidenceHeartbeatRow> Heartbeats { get; init; } = [];
    public IReadOnlyList<EvidencePaymentRow> Payments { get; init; } = [];
}

public sealed record EvidenceMailRow(string Template, string ToEmail, string Status, DateTime CreatedAt, DateTime? SentAt);
public sealed record EvidenceActivationRow(string MachineId, string IpAddress, string AppVersion, string OsVersion, DateTime ActivatedAt, DateTime? LastSeenAt, DateTime? DeactivatedAt);
public sealed record EvidenceHeartbeatRow(string MachineId, string AppVersion, string OsVersion, DateTime FirstSeen, DateTime LastSeen, int Hits);
public sealed record EvidencePaymentRow(string Gateway, string EventType, string PayloadSha256, DateTime CreatedAt);

/// <summary>
/// Chargeback / dispute evidence PDF. Layout follows the admin panel (accent blue, ink navy),
/// not the legacy report. Logo comes from the product website; the issuer name is used when
/// there is no usable image.
/// </summary>
public static class EvidencePdf
{
    const string Ink = "#0f1f3d";
    const string Accent = "#3b6cff";
    const string Muted = "#8b93a7";
    const string Line = "#e8edf5";
    const string Zebra = "#f8fafc";
    const string White = "#ffffff";

    static EvidencePdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Render(EvidenceDocument d)
    {
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.PageColor(White);
                page.DefaultTextStyle(t => t.FontSize(9).FontColor(Ink));
                page.Header().Element(c => Header(c, d));
                page.Content().PaddingTop(12).Element(c => Body(c, d));
                page.Footer().Element(c => Footer(c, d));
            });
        }).GeneratePdf();
    }

    public static IReadOnlyList<string> CopyLines(EvidenceDocument d) =>
    [
        "Transaction evidence report",
        d.IssuerName,
        d.ProductName,
        d.WebsiteUrl,
        d.ReportId,
        d.Key,
        d.CustomerEmail,
        d.PlanLabel,
        "This document compiles the transaction, delivery, device-activation and usage records held for the order below. It is provided as evidence for payment dispute representment."
    ];

    public static string FileName(string key, DateTime utc) =>
        $"evidence_{new string((key ?? "").Where(char.IsLetterOrDigit).ToArray())}_{utc:yyyyMMdd}.pdf";

    static void Header(IContainer container, EvidenceDocument d)
    {
        container.Column(col =>
        {
            col.Item().Background(Accent).Padding(10).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    if (d.LogoBytes is { Length: > 0 })
                    {
                        try
                        {
                            left.Item().Height(28).AlignLeft().Image(d.LogoBytes).FitHeight();
                            left.Item().PaddingTop(3).Text(d.IssuerName).FontSize(8).FontColor(White);
                        }
                        catch (Exception)
                        {
                            NameMark(left, d.IssuerName);
                        }
                    }
                    else
                    {
                        NameMark(left, d.IssuerName);
                    }
                    if (!string.IsNullOrWhiteSpace(d.WebsiteUrl))
                        left.Item().Text(d.WebsiteUrl).FontSize(8).FontColor("#dbe7ff");
                });
                row.ConstantItem(230).AlignRight().Column(right =>
                {
                    right.Item().Text("EVIDENCE REPORT").FontSize(8).FontColor("#dbe7ff").LetterSpacing(0.6f);
                    right.Item().Text("Transaction & usage record").FontSize(13).Bold().FontColor(White);
                    right.Item().PaddingTop(4).Text($"Report {d.ReportId}").FontSize(8).FontColor("#dbe7ff");
                    right.Item().Text($"Generated (UTC) {FDate(d.GeneratedAtUtc)}").FontSize(8).FontColor("#dbe7ff");
                    right.Item().Text($"Prepared by {NA(d.GeneratedBy)}").FontSize(8).FontColor("#dbe7ff");
                });
            });
            col.Item().Background(Ink).Padding(6).Row(row =>
            {
                row.RelativeItem().Text(d.ProductName).FontSize(9).FontColor(White);
                row.ConstantItem(90).AlignRight()
                    .Text(d.Status.ToUpperInvariant()).FontSize(8).Bold().FontColor(White);
            });
        });
    }

    static void NameMark(ColumnDescriptor col, string name)
    {
        col.Item().Text(string.IsNullOrWhiteSpace(name) ? "License evidence" : name)
            .FontSize(16).Bold().FontColor(White);
    }

    static void Body(IContainer container, EvidenceDocument d)
    {
        container.Column(col =>
        {
            col.Spacing(3);
            col.Item().Text(
                    "This document compiles the transaction, delivery, device-activation and usage records held for the order below. It is provided as evidence for payment dispute representment.")
                .FontSize(9).FontColor(Muted).Italic();

            Section(col, "1", "Order & customer");
            KV(col, "Customer name", d.CustomerName);
            KV(col, "Email", d.CustomerEmail);
            KV(col, "Product", d.ProductName);
            KV(col, "Plan", d.PlanLabel);
            KV(col, "Amount charged", d.Amount is decimal amt ? $"{d.Currency} {amt:0.00}" : null);
            KV(col, "Payment gateway", string.IsNullOrWhiteSpace(d.Source) ? "Manual / retail" : d.Source);
            KV(col, "Order ID", d.OrderPublicId);
            KV(col, "Order status", d.OrderStatus);
            KV(col, "Paid at (UTC)", FDateN(d.PaidAt));
            KV(col, "Subscription ID", d.ExternalSubscriptionId);
            KV(col, "Payment ID", d.ExternalPaymentId);
            KV(col, "Support", d.SupportEmail);

            Section(col, "2", "License & delivery");
            KV(col, "License key", d.Key);
            KV(col, "Max devices", d.MaxMachines.ToString());
            KV(col, "Purchased (UTC)", FDate(d.PurchasedAt));
            KV(col, "Issued (UTC)", FDate(d.IssuedAt));
            KV(col, "Activated (UTC)", FDateN(d.ActivatedAt));
            KV(col, "Expires (UTC)", FDateN(d.ExpiresAt));
            KV(col, "Invoice", d.InvoiceNumber);
            KV(col, "Invoice status", d.InvoiceStatus);
            KV(col, "Invoice hash", Trunc(d.InvoiceSha, 20));

            Section(col, "3", "Mail delivery");
            if (d.Mail.Count == 0)
                Empty(col, "No outbound mail is recorded for this key.");
            else
                Table(col,
                    ["Template", "To", "Status", "Queued (UTC)", "Sent (UTC)"],
                    [1.4f, 2.2f, 0.9f, 1.6f, 1.6f],
                    d.Mail.Select(m => new[]
                    {
                        NA(m.Template), NA(m.ToEmail), NA(m.Status), FDate(m.CreatedAt), FDateN(m.SentAt)
                    }).ToList());

            Section(col, "4", "Payment events");
            if (d.Payments.Count == 0)
                Empty(col, "No payment-gateway events are recorded for this order.");
            else
                Table(col,
                    ["Gateway", "Event", "Payload SHA-256", "At (UTC)"],
                    [1.2f, 1.6f, 3.2f, 1.6f],
                    d.Payments.Select(p => new[]
                    {
                        NA(p.Gateway), NA(p.EventType), Trunc(p.PayloadSha256, 20), FDate(p.CreatedAt)
                    }).ToList());

            Section(col, "5", "Device activations");
            if (d.Activations.Count == 0)
                Empty(col, "No device activations recorded for this key.");
            else
                Table(col,
                    ["Machine ID", "IP", "App", "OS", "Activated (UTC)", "Last seen (UTC)"],
                    [2.2f, 1.3f, 0.9f, 1.4f, 1.6f, 1.6f],
                    d.Activations.Select(a => new[]
                    {
                        Trunc(a.MachineId, 22), NA(a.IpAddress), NA(a.AppVersion), Trunc(a.OsVersion, 16),
                        FDate(a.ActivatedAt), FDateN(a.LastSeenAt)
                    }).ToList());

            Section(col, "6", "Usage / heartbeats");
            if (d.Heartbeats.Count == 0)
                Empty(col, "No usage heartbeats recorded for this key.");
            else
                Table(col,
                    ["Machine ID", "App", "OS", "First seen (UTC)", "Last seen (UTC)", "Hits"],
                    [2.2f, 0.9f, 1.4f, 1.6f, 1.6f, 0.6f],
                    d.Heartbeats.Select(h => new[]
                    {
                        Trunc(h.MachineId, 22), NA(h.AppVersion), Trunc(h.OsVersion, 16),
                        FDate(h.FirstSeen), FDate(h.LastSeen), h.Hits.ToString()
                    }).ToList());

            Section(col, "7", "Data provenance");
            Prov(col, "Customer & billing",
                "Taken from the paid order and customer profile stored on this server. Gateway-sourced orders arrive through signed webhooks; the payload hash is listed above.");
            Prov(col, "License key",
                "Minted at purchase or manual issue. The plan term starts on the purchase timestamp, not first activation.");
            Prov(col, "Mail",
                "Queued on this server when the key was issued. Sent-at is when the mail provider accepted the message.");
            Prov(col, "Device activations",
                "Reported by the licensed desktop client over HMAC-signed HTTPS. Machine ID, client IP, app version and OS version are stored server-side.");
            Prov(col, "Usage heartbeats",
                "The desktop client checks in over signed HTTPS while in use. First-seen and last-seen are derived from those check-ins.");
            Prov(col, "Timestamps",
                "All timestamps are stored and shown in Coordinated Universal Time (UTC). This report reflects the data on record at generation time.");
        });
    }

    static void Footer(IContainer container, EvidenceDocument d)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(Line);
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("Confidential — prepared for payment dispute representment. ").FontSize(7).FontColor(Muted);
                    t.Span($"Report {d.ReportId}.").FontSize(7).FontColor(Muted);
                });
                row.ConstantItem(90).AlignRight().Text(t =>
                {
                    t.Span("Page ").FontSize(7).FontColor(Muted);
                    t.CurrentPageNumber().FontSize(7).FontColor(Muted);
                    t.Span(" / ").FontSize(7).FontColor(Muted);
                    t.TotalPages().FontSize(7).FontColor(Muted);
                });
            });
        });
    }

    static void Section(ColumnDescriptor col, string number, string title)
    {
        col.Item().PaddingTop(12).PaddingBottom(4).Row(row =>
        {
            row.ConstantItem(18).Height(18).Background(Accent).AlignCenter().AlignMiddle()
                .Text(number).FontSize(8).Bold().FontColor(White);
            row.RelativeItem().PaddingLeft(8).AlignMiddle()
                .Text(title).FontSize(11).Bold().FontColor(Ink);
        });
        col.Item().LineHorizontal(1).LineColor(Line);
    }

    static void KV(ColumnDescriptor col, string label, string? value)
    {
        col.Item().PaddingVertical(1).Row(row =>
        {
            row.ConstantItem(140).Text(label).FontSize(9).FontColor(Muted);
            row.RelativeItem().Text(NA(value)).FontSize(9);
        });
    }

    static void Empty(ColumnDescriptor col, string text) =>
        col.Item().PaddingTop(4).Text(text).FontSize(9).FontColor(Muted).Italic();

    static void Prov(ColumnDescriptor col, string category, string text)
    {
        col.Item().PaddingTop(5).Text(t =>
        {
            t.Span(category + ". ").Bold().FontSize(9).FontColor(Accent);
            t.Span(text).FontSize(9).FontColor(Ink);
        });
    }

    static void Table(ColumnDescriptor col, string[] headers, float[] weights, List<string[]> rows)
    {
        col.Item().PaddingTop(6).Table(t =>
        {
            t.ColumnsDefinition(cd => { foreach (var w in weights) cd.RelativeColumn(w); });
            foreach (var h in headers)
                t.Cell().Background(Accent).Padding(4).Text(h).FontColor(White).FontSize(8).Bold();
            var alt = false;
            foreach (var row in rows)
            {
                var bg = alt ? Zebra : White;
                foreach (var cell in row)
                    t.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Line).Padding(4).Text(NA(cell)).FontSize(8);
                alt = !alt;
            }
        });
    }

    static string NA(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
    static string FDate(DateTime dt) => DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
    static string FDateN(DateTime? dt) => dt.HasValue ? FDate(dt.Value) : "—";
    static string Trunc(string? s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n] + "…");
}
