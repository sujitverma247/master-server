using System.Text.Json;
using MasterServer.Admin;
using MasterServer.Customers;
using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Support;

public static class SupportApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Map(WebApplication app)
    {
        app.MapPost("/v1/support/tickets", (HttpContext c, AppDb db, CancellationToken ct) => Open(c, db, ct));
        app.MapGet("/v1/support/tickets", (HttpContext c, AppDb db, CancellationToken ct) => PublicGet(c, db, ct));
        app.MapPost("/v1/support/tickets/reply", (HttpContext c, AppDb db, CancellationToken ct) => PublicReply(c, db, ct));

        app.MapGet("/admin/tickets", (HttpContext c, AppDb db, CancellationToken ct) => List(c, db, ct));
        app.MapGet("/admin/tickets/{id:int}", (int id, HttpContext c, AppDb db, CancellationToken ct) => Get(id, c, db, ct));
        app.MapPost("/admin/tickets/reply", (HttpContext c, AppDb db, CancellationToken ct) => StaffReply(c, db, ct));
        app.MapPost("/admin/tickets/close", (HttpContext c, AppDb db, CancellationToken ct) => Close(c, db, ct));
        app.MapPost("/admin/tickets/reopen", (HttpContext c, AppDb db, CancellationToken ct) => Reopen(c, db, ct));
    }

    // ---------- Public ----------

    private static async Task<IResult> Open(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        if (!EmailAddress.TryNormalize(Str(body.Value, "email"), out var email))
            return Fail(400, "invalid email");
        var subject = TicketRules.NormalizeSubject(Str(body.Value, "subject"));
        var text = TicketRules.NormalizeBody(Str(body.Value, "body"));
        if (subject.Length == 0) return Fail(400, "missing subject");
        if (text.Length == 0) return Fail(400, "missing body");

        var customer = await CustomerStore.FindOrCreateAsync(db, email, Str(body.Value, "displayName"), "support", ct);
        await db.SaveChangesAsync(ct);
        int? productId = TryInt(body.Value, "productId", out var pid) ? pid : null;
        if (productId is int p && !await db.Products.AnyAsync(x => x.Id == p, ct))
            return Fail(422, "unknown productId");

        int? licenseId = null;
        var licenseKey = Str(body.Value, "licenseKey").ToUpperInvariant();
        if (licenseKey.Length > 0)
        {
            var license = await db.Licenses.FirstOrDefaultAsync(l => l.Key == licenseKey, ct);
            if (license is null) return Fail(422, "unknown licenseKey");
            licenseId = license.Id;
            productId ??= license.ProductId;
        }

        var ticket = new Ticket
        {
            PublicId = TicketRules.NewPublicId(),
            CustomerId = customer.Id,
            Customer = customer,
            ProductId = productId,
            LicenseId = licenseId,
            Subject = subject,
            Status = TicketStatuses.Open,
            Priority = TicketRules.IsPriority(Str(body.Value, "priority")) ? Str(body.Value, "priority") : TicketPriorities.Normal,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        ticket.Messages.Add(new TicketMessage
        {
            Author = TicketAuthors.Customer,
            AuthorName = customer.DisplayName.Length > 0 ? customer.DisplayName : email,
            Body = text,
            CreatedAt = DateTime.UtcNow
        });
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(ct);
        Audit.Record(db, ctx, null, "ticket.open", $"ticket#{ticket.Id}", $"to={email}");
        await db.SaveChangesAsync(ct);
        return Json(201, new { ticket = PublicView(ticket, includeMessages: true) });
    }

    private static async Task<IResult> PublicGet(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        if (!EmailAddress.TryNormalize(ctx.Request.Query["email"].ToString(), out var email))
            return Fail(400, "invalid email");
        var publicId = ctx.Request.Query["publicId"].ToString().Trim();
        if (publicId.Length == 0) return Fail(400, "missing publicId");

        var ticket = await LoadByPublicId(db, publicId, ct);
        if (ticket is null || !string.Equals(ticket.Customer.Email, email, StringComparison.Ordinal))
            return Fail(404, "ticket not found");
        return Json(200, new { ticket = PublicView(ticket, includeMessages: true) });
    }

    private static async Task<IResult> PublicReply(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        if (!EmailAddress.TryNormalize(Str(body.Value, "email"), out var email))
            return Fail(400, "invalid email");
        var publicId = Str(body.Value, "publicId");
        var text = TicketRules.NormalizeBody(Str(body.Value, "body"));
        if (publicId.Length == 0 || text.Length == 0) return Fail(400, "missing publicId or body");

        var ticket = await LoadByPublicId(db, publicId, ct);
        if (ticket is null || !string.Equals(ticket.Customer.Email, email, StringComparison.Ordinal))
            return Fail(404, "ticket not found");
        if (!TicketRules.CanCustomerReply(ticket.Status))
            return Fail(422, "ticket is closed");

        ticket.Messages.Add(new TicketMessage
        {
            Author = TicketAuthors.Customer,
            AuthorName = ticket.Customer.DisplayName.Length > 0 ? ticket.Customer.DisplayName : email,
            Body = text,
            CreatedAt = DateTime.UtcNow
        });
        ticket.Status = TicketRules.AfterCustomerMessage(ticket.Status);
        ticket.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Json(200, new { ticket = PublicView(ticket, includeMessages: true) });
    }

    // ---------- Admin ----------

    private static async Task<IResult> List(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.SupportRead, ct);
        if (g.Result is not null) return g.Result;

        var q = db.Tickets.AsNoTracking().Include(t => t.Customer).AsQueryable();
        var status = ctx.Request.Query["status"].ToString().Trim().ToLowerInvariant();
        if (TicketRules.IsStatus(status)) q = q.Where(t => t.Status == status);
        if (int.TryParse(ctx.Request.Query["customerId"], out var cid)) q = q.Where(t => t.CustomerId == cid);
        var search = ctx.Request.Query["q"].ToString().Trim();
        if (search.Length > 0)
        {
            var s = search.ToLowerInvariant();
            q = q.Where(t =>
                t.PublicId.ToLower().Contains(s) ||
                t.Subject.ToLower().Contains(s) ||
                t.Customer.Email.ToLower().Contains(s));
        }

        var limit = int.TryParse(ctx.Request.Query["limit"], out var lim) ? Math.Clamp(lim, 1, 1000) : 100;
        var rows = await q.OrderByDescending(t => t.UpdatedAt).Take(limit).ToListAsync(ct);
        return Json(200, new { tickets = rows.Select(t => AdminSummary(t)) });
    }

    private static async Task<IResult> Get(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.SupportRead, ct);
        if (g.Result is not null) return g.Result;

        var ticket = await db.Tickets.AsNoTracking()
            .Include(t => t.Customer)
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return Fail(404, "ticket not found");
        return Json(200, new { ticket = AdminDetail(ticket) });
    }

    private static async Task<IResult> StaffReply(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.SupportWrite, ct);
        if (g.Result is not null) return g.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        if (!TryInt(body.Value, "id", out var id)) return Fail(400, "missing id");
        var text = TicketRules.NormalizeBody(Str(body.Value, "body"));
        if (text.Length == 0) return Fail(400, "missing body");

        var ticket = await db.Tickets.Include(t => t.Customer).Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return Fail(404, "ticket not found");
        if (ticket.Status == TicketStatuses.Closed)
            return Fail(422, "ticket is closed");

        ticket.Messages.Add(new TicketMessage
        {
            Author = TicketAuthors.Staff,
            AuthorName = g.Principal!.User.DisplayName.Length > 0 ? g.Principal.User.DisplayName : g.Principal.User.Username,
            Body = text,
            CreatedAt = DateTime.UtcNow
        });
        ticket.Status = TicketRules.AfterStaffMessage(ticket.Status);
        ticket.UpdatedAt = DateTime.UtcNow;
        Audit.Record(db, ctx, g.Principal, "ticket.reply", $"ticket#{ticket.Id}");
        await db.SaveChangesAsync(ct);
        return Json(200, new { ticket = AdminDetail(ticket) });
    }

    private static async Task<IResult> Close(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.SupportWrite, ct);
        if (g.Result is not null) return g.Result;
        var ticket = await FindTicket(ctx, db, ct);
        if (ticket is null) return Fail(404, "ticket not found");
        ticket.Status = TicketStatuses.Closed;
        ticket.UpdatedAt = DateTime.UtcNow;
        Audit.Record(db, ctx, g.Principal, "ticket.close", $"ticket#{ticket.Id}");
        await db.SaveChangesAsync(ct);
        return Json(200, new { ticket = AdminSummary(ticket) });
    }

    private static async Task<IResult> Reopen(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.SupportWrite, ct);
        if (g.Result is not null) return g.Result;
        var ticket = await FindTicket(ctx, db, ct);
        if (ticket is null) return Fail(404, "ticket not found");
        ticket.Status = TicketStatuses.Open;
        ticket.UpdatedAt = DateTime.UtcNow;
        Audit.Record(db, ctx, g.Principal, "ticket.reopen", $"ticket#{ticket.Id}");
        await db.SaveChangesAsync(ct);
        return Json(200, new { ticket = AdminSummary(ticket) });
    }

    private static async Task<Ticket?> FindTicket(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return null;
        if (TryInt(body.Value, "id", out var id))
            return await db.Tickets.Include(t => t.Customer).FirstOrDefaultAsync(t => t.Id == id, ct);
        return null;
    }

    private static async Task<Ticket?> LoadByPublicId(AppDb db, string publicId, CancellationToken ct) =>
        await db.Tickets.Include(t => t.Customer).Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.PublicId == publicId, ct);

    private static object PublicView(Ticket t, bool includeMessages) => new
    {
        publicId = t.PublicId,
        t.Subject, t.Status, t.Priority,
        createdAt = t.CreatedAt.ToString("o"),
        updatedAt = t.UpdatedAt.ToString("o"),
        messages = includeMessages ? t.Messages.OrderBy(m => m.Id).Select(MessageView) : null
    };

    private static object AdminSummary(Ticket t) => new
    {
        t.Id, t.PublicId, t.CustomerId, customerEmail = t.Customer?.Email,
        t.ProductId, t.LicenseId, t.Subject, t.Status, t.Priority,
        createdAt = t.CreatedAt.ToString("o"),
        updatedAt = t.UpdatedAt.ToString("o")
    };

    private static object AdminDetail(Ticket t) => new
    {
        t.Id, t.PublicId, t.CustomerId, customerEmail = t.Customer?.Email,
        t.ProductId, t.LicenseId, t.Subject, t.Status, t.Priority,
        createdAt = t.CreatedAt.ToString("o"),
        updatedAt = t.UpdatedAt.ToString("o"),
        messages = t.Messages.OrderBy(m => m.Id).Select(MessageView)
    };

    private static object MessageView(TicketMessage m) => new
    {
        m.Id, m.Author, m.AuthorName, m.Body, createdAt = m.CreatedAt.ToString("o")
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

    private static string Str(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "").Trim() : "";

    private static bool TryInt(JsonElement body, string name, out int result)
    {
        result = 0;
        return body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out result);
    }

    private static IResult Fail(int status, string error) => Json(status, new { error });
    private static IResult Json(int status, object body) => Results.Json(body, JsonOptions, statusCode: status);
}
