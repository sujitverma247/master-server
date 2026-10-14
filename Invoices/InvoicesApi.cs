using System.Text.Json;
using MasterServer.Admin;
using MasterServer.Customers;
using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Invoices;

public static class InvoicesApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Map(WebApplication app)
    {
        app.MapGet("/v1/invoices", (HttpContext c, AppDb db, CancellationToken ct) => PublicGet(c, db, ct));
        app.MapGet("/v1/invoices/pdf", (HttpContext c, AppDb db, CancellationToken ct) => PublicPdf(c, db, ct));
        app.MapGet("/v1/evidence", (HttpContext c, AppDb db, CancellationToken ct) => PublicEvidence(c, db, ct));

        app.MapGet("/admin/invoices", (HttpContext c, AppDb db, CancellationToken ct) => List(c, db, ct));
        app.MapGet("/admin/invoices/{id:int}", (int id, HttpContext c, AppDb db, CancellationToken ct) => Get(id, c, db, ct));
        app.MapGet("/admin/invoices/{id:int}/pdf", (int id, HttpContext c, AppDb db, CancellationToken ct) => AdminPdf(id, c, db, ct));
        app.MapPost("/admin/invoices/void", (HttpContext c, AppDb db, CancellationToken ct) => Void(c, db, ct));
        app.MapGet("/admin/orders/{id:int}/evidence", (int id, HttpContext c, AppDb db, CancellationToken ct) => AdminEvidence(id, c, db, ct));
    }

    private static async Task<IResult> PublicGet(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        if (!TryPublicQuery(ctx, out var email, out var publicId, out var orderPublicId, out var error))
            return Fail(error.status, error.message);
        var invoice = await FindPublicAsync(db, email, publicId, orderPublicId, ct);
        if (invoice is null) return Fail(404, "invoice not found");
        return Json(200, new { invoice = PublicView(invoice) });
    }

    private static async Task<IResult> PublicPdf(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        if (!TryPublicQuery(ctx, out var email, out var publicId, out var orderPublicId, out var error))
            return Fail(error.status, error.message);
        var invoice = await FindPublicAsync(db, email, publicId, orderPublicId, ct);
        if (invoice is null) return Fail(404, "invoice not found");
        return PdfFile(invoice);
    }

    private static async Task<IResult> PublicEvidence(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        if (!EmailAddress.TryNormalize(ctx.Request.Query["email"].ToString(), out var email))
            return Fail(400, "invalid email");
        var orderPublicId = ctx.Request.Query["orderPublicId"].ToString().Trim();
        if (orderPublicId.Length == 0) return Fail(400, "missing orderPublicId");

        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.PublicId == orderPublicId, ct);
        if (order is null || !string.Equals(order.Customer.Email, email, StringComparison.Ordinal))
            return Fail(404, "order not found");
        return Json(200, new { evidence = await EvidencePack.BuildAsync(db, order, includeLicenseId: false, ct) });
    }

    private static async Task<IResult> List(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.InvoiceRead, ct);
        if (g.Result is not null) return g.Result;

        var q = db.Invoices.AsNoTracking().AsQueryable();
        var status = ctx.Request.Query["status"].ToString().Trim().ToLowerInvariant();
        if (status is InvoiceStatuses.Issued or InvoiceStatuses.Void) q = q.Where(i => i.Status == status);
        if (int.TryParse(ctx.Request.Query["customerId"], out var cid)) q = q.Where(i => i.CustomerId == cid);
        if (int.TryParse(ctx.Request.Query["orderId"], out var oid)) q = q.Where(i => i.OrderId == oid);
        var search = ctx.Request.Query["q"].ToString().Trim();
        if (search.Length > 0)
            q = q.Where(i => i.PublicId == search || i.Number == search || i.OrderPublicId == search);

        var limit = int.TryParse(ctx.Request.Query["limit"], out var lim) ? Math.Clamp(lim, 1, 1000) : 100;
        var rows = await q.OrderByDescending(i => i.Id).Take(limit).ToListAsync(ct);
        return Json(200, new { invoices = rows.Select(AdminSummary) });
    }

    private static async Task<IResult> Get(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.InvoiceRead, ct);
        if (g.Result is not null) return g.Result;
        var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return Fail(404, "invoice not found");
        return Json(200, new { invoice = AdminDetail(invoice) });
    }

    private static async Task<IResult> AdminPdf(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.InvoiceRead, ct);
        if (g.Result is not null) return g.Result;
        var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return Fail(404, "invoice not found");
        Audit.Record(db, ctx, g.Principal, "invoice.pdf", $"invoice#{invoice.Id}", invoice.Number);
        await db.SaveChangesAsync(ct);
        return PdfFile(invoice);
    }

    private static async Task<IResult> Void(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.InvoiceWrite, ct);
        if (g.Result is not null) return g.Result;
        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        if (!TryInt(body.Value, "id", out var id)) return Fail(400, "missing id");
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return Fail(404, "invoice not found");
        await InvoiceIssuer.VoidForOrderAsync(db, invoice.OrderId, ct);
        Audit.Record(db, ctx, g.Principal, "invoice.void", $"invoice#{invoice.Id}", invoice.Number);
        await db.SaveChangesAsync(ct);
        await db.Entry(invoice).ReloadAsync(ct);
        return Json(200, new { invoice = AdminSummary(invoice) });
    }

    private static async Task<IResult> AdminEvidence(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.InvoiceRead, ct);
        if (g.Result is not null) return g.Result;
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return Fail(404, "order not found");
        Audit.Record(db, ctx, g.Principal, "evidence.read", $"order#{order.Id}");
        await db.SaveChangesAsync(ct);
        return Json(200, new { evidence = await EvidencePack.BuildAsync(db, order, includeLicenseId: true, ct) });
    }

    private static bool TryPublicQuery(HttpContext ctx, out string email, out string publicId, out string orderPublicId,
        out (int status, string message) error)
    {
        email = "";
        publicId = ctx.Request.Query["publicId"].ToString().Trim();
        orderPublicId = ctx.Request.Query["orderPublicId"].ToString().Trim();
        error = (400, "");
        if (!EmailAddress.TryNormalize(ctx.Request.Query["email"].ToString(), out email))
        {
            error = (400, "invalid email");
            return false;
        }
        if (publicId.Length == 0 && orderPublicId.Length == 0)
        {
            error = (400, "missing publicId or orderPublicId");
            return false;
        }
        return true;
    }

    private static async Task<Invoice?> FindPublicAsync(AppDb db, string email, string publicId, string orderPublicId, CancellationToken ct)
    {
        Invoice? invoice = null;
        if (publicId.Length > 0)
            invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.PublicId == publicId, ct);
        else if (orderPublicId.Length > 0)
            invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.OrderPublicId == orderPublicId, ct);
        if (invoice is null || !string.Equals(invoice.CustomerEmail, email, StringComparison.Ordinal))
            return null;
        return invoice;
    }

    private static IResult PdfFile(Invoice invoice) =>
        Results.File(invoice.PdfBytes, "application/pdf", $"invoice-{invoice.Number}.pdf");

    private static object PublicView(Invoice i) => new
    {
        i.PublicId, i.Number, i.Status, i.PlanType, amount = i.Amount, i.Currency,
        i.OrderPublicId, i.Gateway, i.PolicyVersion, i.PdfSha256,
        issuedAt = i.IssuedAt.ToString("o"),
        voidedAt = i.VoidedAt?.ToString("o")
    };

    private static object AdminSummary(Invoice i) => new
    {
        i.Id, i.PublicId, i.Number, i.OrderId, i.CustomerId, i.CustomerEmail,
        i.Status, i.PlanType, amount = i.Amount, i.Currency, i.OrderPublicId, i.PdfSha256,
        issuedAt = i.IssuedAt.ToString("o"),
        voidedAt = i.VoidedAt?.ToString("o")
    };

    private static object AdminDetail(Invoice i) => new
    {
        i.Id, i.PublicId, i.Number, i.OrderId, i.CustomerId, i.CustomerEmail, i.CustomerName,
        i.ProductName, i.BrandName, i.SupportEmail, i.PlanType, amount = i.Amount, i.Currency,
        i.Gateway, i.OrderPublicId, i.PolicyVersion, i.Status, i.PdfSha256,
        issuedAt = i.IssuedAt.ToString("o"),
        voidedAt = i.VoidedAt?.ToString("o")
    };

    private readonly record struct Gate(IResult? Result, AdminPrincipal? Principal);

    private static async Task<Gate> Require(HttpContext ctx, AppDb db, string permission, CancellationToken ct)
    {
        var principal = await AdminAuth.ResolveAsync(ctx, db, ct);
        if (principal is null) return new Gate(Fail(401, "unauthorized"), null);
        if (!principal.Has(permission)) return new Gate(Fail(403, "forbidden"), null);
        return new Gate(null, principal);
    }

    private static async Task<JsonElement?> ReadBodyAsync(HttpContext ctx, CancellationToken ct)
    {
        try { using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ct); return doc.RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    private static bool TryInt(JsonElement body, string name, out int result)
    {
        result = 0;
        return body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out result);
    }

    private static IResult Fail(int status, string error) => Json(status, new { error });
    private static IResult Json(int status, object body) => Results.Json(body, JsonOptions, statusCode: status);
}
