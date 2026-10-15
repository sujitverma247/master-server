using System.Security.Cryptography;
using MasterServer.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

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
    string PolicyVersion,
    string WebsiteUrl = "",
    string CustomerPhone = "",
    string PlanLabel = "",
    DateTime? PaidAt = null,
    string? ExternalPaymentId = null,
    DateTime? VoidedAt = null,
    byte[]? LogoBytes = null);

/// <summary>
/// Standard tax-invoice layout (A4). Logo comes from the product website; the seller name is used
/// when there is no usable image. Labels are brand-neutral; names come from the snapshot.
/// </summary>
public static class InvoicePdf
{
    const string Ink = "#0f1f3d";
    const string Accent = "#3b6cff";
    const string Muted = "#8b93a7";
    const string Line = "#e8edf5";
    const string Zebra = "#f8fafc";
    const string White = "#ffffff";
    const string Danger = "#dc2626";

    static InvoicePdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

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
        invoice.PolicyVersion,
        VoidedAt: invoice.VoidedAt);

    public static bool IsCancelled(string status) =>
        string.Equals(status, InvoiceStatuses.Void, StringComparison.OrdinalIgnoreCase);

    public static string StatusLabel(string status) =>
        IsCancelled(status) ? "Cancelled" : "Paid";

    public static string PlanLabel(string planType, string? catalogName = null)
    {
        if (!string.IsNullOrWhiteSpace(catalogName)) return catalogName.Trim();
        return planType.Trim().ToLowerInvariant() switch
        {
            "monthly" => "Monthly",
            "1year" => "1 Year",
            "2year" => "2 Years",
            "3year" => "3 Years",
            "5year" => "5 Years",
            "lifetime" => "Lifetime",
            _ => string.IsNullOrWhiteSpace(planType) ? "License" : planType
        };
    }

    public static IReadOnlyList<string> Lines(InvoiceSnapshot s)
    {
        var plan = string.IsNullOrWhiteSpace(s.PlanLabel) ? PlanLabel(s.PlanType) : s.PlanLabel;
        var seller = string.IsNullOrWhiteSpace(s.BrandName) ? "Seller" : s.BrandName;
        return
        [
            "Invoice",
            s.Number,
            StatusLabel(s.Status),
            $"Issued {s.IssuedAt:yyyy-MM-dd}",
            seller,
            s.WebsiteUrl,
            s.SupportEmail,
            "Bill to",
            s.CustomerName,
            s.CustomerEmail,
            s.CustomerPhone,
            s.ProductName,
            plan,
            $"1 × {Money(s.Amount, s.Currency)}",
            $"Total {Money(s.Amount, s.Currency)}",
            $"Order {s.OrderPublicId}",
            string.IsNullOrWhiteSpace(s.Gateway) ? "Payment provider" : s.Gateway,
            string.IsNullOrEmpty(s.PolicyVersion) ? "" : $"Policy {s.PolicyVersion}",
            "Taxes, if any, were collected by the payment provider.",
            IsCancelled(s.Status)
                ? "This invoice is cancelled and is not a claim for payment."
                : "This invoice records a payment collected by the payment provider."
        ];
    }

    public static byte[] Render(InvoiceSnapshot snapshot)
    {
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.PageColor(White);
                page.DefaultTextStyle(t => t.FontSize(9).FontColor(Ink).FontFamily("Helvetica"));
                page.Header().Element(c => Header(c, snapshot));
                page.Content().PaddingTop(16).Element(c => Body(c, snapshot));
                page.Footer().Element(c => Footer(c, snapshot));
            });
        }).GeneratePdf();
    }

    public static string Sha256Hex(byte[] pdf) =>
        Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant();

    static void Header(IContainer container, InvoiceSnapshot s)
    {
        var seller = string.IsNullOrWhiteSpace(s.BrandName) ? "Seller" : s.BrandName;
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    if (s.LogoBytes is { Length: > 0 })
                    {
                        try
                        {
                            left.Item().Height(36).AlignLeft().Image(s.LogoBytes).FitHeight();
                            left.Item().PaddingTop(4).Text(seller).FontSize(11).Bold().FontColor(Ink);
                        }
                        catch (Exception)
                        {
                            left.Item().Text(seller).FontSize(18).Bold().FontColor(Ink);
                        }
                    }
                    else
                    {
                        left.Item().Text(seller).FontSize(18).Bold().FontColor(Ink);
                    }
                    if (!string.IsNullOrWhiteSpace(s.WebsiteUrl))
                        left.Item().Text(s.WebsiteUrl).FontSize(8).FontColor(Muted);
                    if (!string.IsNullOrWhiteSpace(s.SupportEmail))
                        left.Item().Text(s.SupportEmail).FontSize(8).FontColor(Muted);
                });

                row.ConstantItem(220).AlignRight().Column(right =>
                {
                    right.Item().Text("INVOICE").FontSize(22).Bold().FontColor(Accent);
                    right.Item().PaddingTop(4).Text(s.Number).FontSize(12).Bold().FontColor(Ink);
                    right.Item().PaddingTop(6).Text($"Date  {s.IssuedAt:dd MMM yyyy}").FontSize(9).FontColor(Muted);
                    if (s.PaidAt is DateTime paid)
                        right.Item().Text($"Paid  {paid:dd MMM yyyy}").FontSize(9).FontColor(Muted);
                    right.Item().PaddingTop(6).AlignRight().Element(c => StatusPill(c, s));
                });
            });

            if (IsCancelled(s.Status))
            {
                col.Item().PaddingTop(12).Background("#fef2f2").Border(1).BorderColor("#fecaca").Padding(8)
                    .Text("CANCELLED — this invoice is not a claim for payment. A refund, if any, is processed by the payment provider.")
                    .FontSize(9).FontColor(Danger);
            }

            col.Item().PaddingTop(12).LineHorizontal(1).LineColor(Line);
        });
    }

    static void StatusPill(IContainer container, InvoiceSnapshot s)
    {
        var cancelled = IsCancelled(s.Status);
        container.Background(cancelled ? "#fef2f2" : "#e7f8ee")
            .PaddingVertical(3).PaddingHorizontal(8)
            .Text(StatusLabel(s.Status).ToUpperInvariant())
            .FontSize(8).Bold().FontColor(cancelled ? Danger : "#16a34a");
    }

    static void Body(IContainer container, InvoiceSnapshot s)
    {
        var plan = string.IsNullOrWhiteSpace(s.PlanLabel) ? PlanLabel(s.PlanType) : s.PlanLabel;
        var description = string.IsNullOrWhiteSpace(s.ProductName) ? plan : $"{s.ProductName} — {plan}";
        var money = Money(s.Amount, s.Currency);

        container.Column(col =>
        {
            col.Spacing(14);

            col.Item().Row(row =>
            {
                row.RelativeItem().Column(bill =>
                {
                    bill.Item().Text("BILL TO").FontSize(8).Bold().FontColor(Muted).LetterSpacing(0.5f);
                    bill.Item().PaddingTop(4).Text(NA(s.CustomerName)).FontSize(11).Bold();
                    bill.Item().Text(NA(s.CustomerEmail)).FontSize(9).FontColor(Muted);
                    if (!string.IsNullOrWhiteSpace(s.CustomerPhone))
                        bill.Item().Text(s.CustomerPhone).FontSize(9).FontColor(Muted);
                });
                row.ConstantItem(16);
                row.RelativeItem().Column(pay =>
                {
                    pay.Item().Text("PAYMENT").FontSize(8).Bold().FontColor(Muted).LetterSpacing(0.5f);
                    pay.Item().PaddingTop(4).Element(c => KV(c, "Provider", GatewayLabel(s.Gateway)));
                    pay.Item().Element(c => KV(c, "Order", s.OrderPublicId));
                    if (!string.IsNullOrWhiteSpace(s.ExternalPaymentId))
                        pay.Item().Element(c => KV(c, "Payment ID", s.ExternalPaymentId));
                    if (!string.IsNullOrWhiteSpace(s.PolicyVersion))
                        pay.Item().Element(c => KV(c, "Policy", s.PolicyVersion));
                });
            });

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(5);
                    columns.ConstantColumn(50);
                    columns.ConstantColumn(90);
                    columns.ConstantColumn(90);
                });

                table.Header(header =>
                {
                    header.Cell().Background(Ink).Padding(8).Text("Description").FontColor(White).Bold();
                    header.Cell().Background(Ink).Padding(8).AlignRight().Text("Qty").FontColor(White).Bold();
                    header.Cell().Background(Ink).Padding(8).AlignRight().Text("Unit price").FontColor(White).Bold();
                    header.Cell().Background(Ink).Padding(8).AlignRight().Text("Amount").FontColor(White).Bold();
                });

                table.Cell().BorderBottom(1).BorderColor(Line).Background(Zebra).Padding(8).Text(description);
                table.Cell().BorderBottom(1).BorderColor(Line).Background(Zebra).Padding(8).AlignRight().Text("1");
                table.Cell().BorderBottom(1).BorderColor(Line).Background(Zebra).Padding(8).AlignRight().Text(money);
                table.Cell().BorderBottom(1).BorderColor(Line).Background(Zebra).Padding(8).AlignRight().Text(money);
            });

            col.Item().AlignRight().Width(250).Column(totals =>
            {
                totals.Item().Row(r =>
                {
                    r.RelativeItem().Text("Subtotal").FontColor(Muted);
                    r.ConstantItem(100).AlignRight().Text(money);
                });
                totals.Item().PaddingTop(4).Row(r =>
                {
                    r.RelativeItem().Text("Tax").FontColor(Muted);
                    r.ConstantItem(100).AlignRight().Text("Included*").FontColor(Muted);
                });
                totals.Item().PaddingTop(6).LineHorizontal(1).LineColor(Line);
                totals.Item().PaddingTop(6).Row(r =>
                {
                    r.RelativeItem().Text("Total").FontSize(12).Bold();
                    r.ConstantItem(100).AlignRight().Text(money).FontSize(12).Bold().FontColor(Accent);
                });
                totals.Item().PaddingTop(2).Row(r =>
                {
                    r.RelativeItem().Text(IsCancelled(s.Status) ? "Amount due" : "Amount paid").FontColor(Muted);
                    r.ConstantItem(100).AlignRight().Text(IsCancelled(s.Status) ? Money(0, s.Currency) : money);
                });
            });

            col.Item().PaddingTop(8).Text("* Taxes, if any, were collected by the payment provider. This document is a record of that charge; it is not a tax invoice from a local tax authority.")
                .FontSize(8).FontColor(Muted).Italic();
            col.Item().Text(IsCancelled(s.Status)
                    ? "Cancelled invoices stay on file for the original payment. Refunds are processed on the payment provider, not from this page."
                    : "Thank you for your purchase. Keep this invoice with your records.")
                .FontSize(8).FontColor(Muted);
        });
    }

    static void Footer(IContainer container, InvoiceSnapshot s)
    {
        var seller = string.IsNullOrWhiteSpace(s.BrandName) ? "Seller" : s.BrandName;
        container.Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(Line);
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text(seller).FontSize(8).FontColor(Muted);
                row.RelativeItem().AlignCenter().Text(s.Number).FontSize(8).FontColor(Muted);
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.Span("Page ").FontSize(8).FontColor(Muted);
                    text.CurrentPageNumber().FontSize(8).FontColor(Muted);
                    text.Span(" of ").FontSize(8).FontColor(Muted);
                    text.TotalPages().FontSize(8).FontColor(Muted);
                });
            });
        });
    }

    static void KV(IContainer container, string label, string value)
    {
        container.Row(row =>
        {
            row.ConstantItem(72).Text(label).FontSize(8).FontColor(Muted);
            row.RelativeItem().Text(NA(value)).FontSize(9);
        });
    }

    static string Money(decimal amount, string currency)
    {
        var code = (currency ?? "").Trim().ToUpperInvariant();
        var value = amount.ToString("0.00");
        return code switch
        {
            "EUR" => $"€{value}",
            "USD" => $"${value}",
            "GBP" => $"£{value}",
            "INR" => $"INR {value}",
            "" => value,
            _ => $"{code} {value}"
        };
    }

    static string GatewayLabel(string gateway)
    {
        if (string.IsNullOrWhiteSpace(gateway)) return "Payment provider";
        if (string.Equals(gateway, "dev", StringComparison.OrdinalIgnoreCase)) return "Test gateway";
        if (string.Equals(gateway, "manual", StringComparison.OrdinalIgnoreCase)) return "Manual / offline";
        return char.ToUpperInvariant(gateway[0]) + gateway[1..];
    }

    static string NA(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}
