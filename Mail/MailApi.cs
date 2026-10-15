using System.Text.Json;
using MasterServer.Admin;
using MasterServer.Catalog;
using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Mail;

public static class MailApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Map(WebApplication app, IMailSender sender)
    {
        app.MapGet("/admin/mail", (HttpContext c, AppDb db, CancellationToken ct) => List(c, db, ct));
        app.MapGet("/admin/mail/{id:long}", (long id, HttpContext c, AppDb db, CancellationToken ct) => Get(id, c, db, ct));
        app.MapPost("/admin/mail/resend", (HttpContext c, AppDb db, CancellationToken ct) => Resend(c, db, sender, ct));
        app.MapPost("/admin/mail/flush", (HttpContext c, AppDb db, CancellationToken ct) => Flush(c, db, sender, ct));
    }

    private static async Task<IResult> List(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.MailRead, ct);
        if (g.Result is not null) return g.Result;

        var q = db.MailOutbox.AsNoTracking().AsQueryable();
        var template = ctx.Request.Query["template"].ToString().Trim();
        if (template.Length > 0) q = q.Where(m => m.Template == template);
        var status = ctx.Request.Query["status"].ToString().Trim().ToLowerInvariant();
        if (status is MailStatuses.Pending or MailStatuses.Sent or MailStatuses.Failed)
            q = q.Where(m => m.Status == status);
        if (int.TryParse(ctx.Request.Query["customerId"], out var cid) && cid > 0)
            q = q.Where(m => m.CustomerId == cid);
        if (int.TryParse(ctx.Request.Query["orderId"], out var oid) && oid > 0)
            q = q.Where(m => m.OrderId == oid);

        var search = ctx.Request.Query["q"].ToString().Trim();
        if (search.Length > 0)
        {
            var s = search.ToLowerInvariant();
            q = q.Where(m =>
                m.ToEmail.ToLower().Contains(s)
                || m.ToName.ToLower().Contains(s)
                || m.FromEmail.ToLower().Contains(s)
                || m.Subject.ToLower().Contains(s)
                || m.Template.ToLower().Contains(s)
                || m.Provider.ToLower().Contains(s)
                || m.LastError.ToLower().Contains(s));
        }

        if (DateOnly.TryParse(ctx.Request.Query["from"], out var fromDay))
        {
            var fromUtc = fromDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(m => m.CreatedAt >= fromUtc);
        }
        if (DateOnly.TryParse(ctx.Request.Query["to"], out var toDay))
        {
            var toExclusive = toDay.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(m => m.CreatedAt < toExclusive);
        }

        var projected = q.Select(m => new
        {
            m.Id, m.Template, m.ToEmail, m.ToName, m.FromEmail, m.Subject, m.Locale, m.Status, m.Provider,
            m.Attempts, m.LastError, m.OrderId, m.LicenseId, m.CustomerId, m.CreatedAt, m.SentAt
        });

        var sort = ctx.Request.Query["sort"].ToString().Trim().ToLowerInvariant();
        var asc = ctx.Request.Query["dir"].ToString().Trim().Equals("asc", StringComparison.OrdinalIgnoreCase);
        projected = sort switch
        {
            "email" or "toemail" => asc ? projected.OrderBy(x => x.ToEmail) : projected.OrderByDescending(x => x.ToEmail),
            "template" => asc ? projected.OrderBy(x => x.Template) : projected.OrderByDescending(x => x.Template),
            "subject" => asc ? projected.OrderBy(x => x.Subject) : projected.OrderByDescending(x => x.Subject),
            "status" => asc ? projected.OrderBy(x => x.Status) : projected.OrderByDescending(x => x.Status),
            "provider" => asc ? projected.OrderBy(x => x.Provider) : projected.OrderByDescending(x => x.Provider),
            "attempts" => asc ? projected.OrderBy(x => x.Attempts) : projected.OrderByDescending(x => x.Attempts),
            "sentat" => asc ? projected.OrderBy(x => x.SentAt) : projected.OrderByDescending(x => x.SentAt),
            _ => asc ? projected.OrderBy(x => x.CreatedAt) : projected.OrderByDescending(x => x.CreatedAt)
        };

        var total = await projected.CountAsync(ct);
        var limit = CatalogPaging.NormalizeLimit(ctx.Request.Query["limit"].ToString());
        var page = CatalogPaging.ClampPage(CatalogPaging.NormalizePage(ctx.Request.Query["page"].ToString()), total, limit);
        var rows = await projected.Skip((page - 1) * limit).Take(limit).ToListAsync(ct);

        var allCount = await db.MailOutbox.CountAsync(ct);
        var pendingCount = await db.MailOutbox.CountAsync(m => m.Status == MailStatuses.Pending, ct);
        var sentCount = await db.MailOutbox.CountAsync(m => m.Status == MailStatuses.Sent, ct);
        var failedCount = await db.MailOutbox.CountAsync(m => m.Status == MailStatuses.Failed, ct);
        var templates = await db.MailOutbox.AsNoTracking()
            .Where(m => m.Template != "")
            .Select(m => m.Template)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(ct);

        return Json(200, new
        {
            mail = rows.Select(x => new
            {
                x.Id, x.Template, x.ToEmail, x.ToName, x.FromEmail, x.Subject, x.Locale, x.Status, x.Provider,
                x.Attempts, x.LastError, x.OrderId, x.LicenseId, x.CustomerId,
                createdAt = x.CreatedAt.ToString("o"),
                sentAt = x.SentAt?.ToString("o")
            }),
            total,
            page,
            limit,
            counts = new { total = allCount, pending = pendingCount, sent = sentCount, failed = failedCount },
            templates
        });
    }

    private static async Task<IResult> Get(long id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.MailRead, ct);
        if (g.Result is not null) return g.Result;

        var row = await db.MailOutbox.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (row is null) return Fail(404, "mail not found");
        return Json(200, new { mail = Detail(row) });
    }

    private static async Task<IResult> Resend(HttpContext ctx, AppDb db, IMailSender sender, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.MailWrite, ct);
        if (g.Result is not null) return g.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        if (!TryLong(body.Value, "id", out var id)) return Fail(400, "missing id");

        var row = await db.MailOutbox.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (row is null) return Fail(404, "mail not found");

        row.Status = MailStatuses.Pending;
        row.LastError = "";
        await db.SaveChangesAsync(ct);
        await MailDispatch.DrainAsync(db, sender, 20, ct);
        await db.Entry(row).ReloadAsync(ct);
        Audit.Record(db, ctx, g.Principal, "mail.resend", $"mail#{row.Id}", $"to={row.ToEmail},template={row.Template}");
        await db.SaveChangesAsync(ct);
        return Json(200, new { mail = Detail(row) });
    }

    private static async Task<IResult> Flush(HttpContext ctx, AppDb db, IMailSender sender, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.MailWrite, ct);
        if (g.Result is not null) return g.Result;

        var sent = await MailDispatch.DrainAsync(db, sender, 100, ct);
        Audit.Record(db, ctx, g.Principal, "mail.flush", "", $"sent={sent}");
        await db.SaveChangesAsync(ct);
        return Json(200, new { sent });
    }

    private static object Detail(OutboundMail m) => new
    {
        m.Id, m.Template, m.IdempotencyKey, m.ToEmail, m.ToName, m.FromEmail, m.FromName,
        m.Subject, m.Body, m.Locale, m.Status, m.Provider, m.ProviderMessageId, m.Attempts, m.LastError,
        m.ProductId, m.CustomerId, m.OrderId, m.LicenseId,
        createdAt = m.CreatedAt.ToString("o"),
        sentAt = m.SentAt?.ToString("o")
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

    private static bool TryLong(JsonElement body, string name, out long result)
    {
        result = 0;
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(name, out var v)) return false;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out result)) return true;
        return false;
    }

    private static IResult Fail(int status, string error) => Json(status, new { error });
    private static IResult Json(int status, object body) => Results.Json(body, JsonOptions, statusCode: status);
}
