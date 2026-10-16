using System.Text.Json;
using MasterServer.Admin;
using MasterServer.Catalog;
using MasterServer.Customers;
using MasterServer.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
        var displayName = FirstNonEmpty(Str(body.Value, "displayName"), Str(body.Value, "name"), Str(body.Value, "fullName"));
        var text = TicketRules.NormalizeBody(FirstNonEmpty(
            Str(body.Value, "body"),
            Str(body.Value, "issue"),
            Str(body.Value, "message"),
            Str(body.Value, "description")));
        var subject = TicketRules.NormalizeSubject(Str(body.Value, "subject"));
        if (subject.Length == 0) subject = TicketRules.SubjectFromIssue(text);
        if (text.Length == 0) return Fail(400, "missing issue");
        if (subject.Length == 0) return Fail(400, "missing issue");

        var customer = await CustomerStore.FindOrCreateAsync(db, email, displayName, "support", ct);
        CustomerStore.ApplyPhone(customer, FirstNonEmpty(Str(body.Value, "phone"), Str(body.Value, "phoneNumber")));
        CustomerStore.ApplyLocale(customer, FirstNonEmpty(Str(body.Value, "language"), Str(body.Value, "locale"), Str(body.Value, "lang")));
        await db.SaveChangesAsync(ct);

        Product? product = null;
        if (TryInt(body.Value, "productId", out var pid))
            product = await db.Products.Include(p => p.Brand).FirstOrDefaultAsync(x => x.Id == pid, ct);
        if (product is null)
        {
            var productSlug = FirstNonEmpty(Str(body.Value, "productSlug"), Str(body.Value, "slug"));
            if (productSlug.Length > 0)
                product = await db.Products.Include(p => p.Brand).FirstOrDefaultAsync(x => x.Slug == productSlug, ct);
        }
        if (product is null)
        {
            var appKey = Str(body.Value, "appKey");
            if (appKey.Length > 0)
                product = await db.Products.Include(p => p.Brand).FirstOrDefaultAsync(x => x.AppKey == appKey, ct);
        }
        if (TryInt(body.Value, "productId", out _) && product is null)
            return Fail(422, "unknown productId");

        int? licenseId = null;
        var licenseKey = Str(body.Value, "licenseKey").ToUpperInvariant();
        if (licenseKey.Length > 0)
        {
            var license = await db.Licenses.FirstOrDefaultAsync(l => l.Key == licenseKey, ct);
            if (license is null) return Fail(422, "unknown licenseKey");
            licenseId = license.Id;
            if (product is null)
                product = await db.Products.Include(p => p.Brand).FirstOrDefaultAsync(x => x.Id == license.ProductId, ct);
        }

        Brand? brand = product?.Brand;
        if (TryInt(body.Value, "brandId", out var bid))
        {
            brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == bid, ct);
            if (brand is null) return Fail(422, "unknown brandId");
        }
        else
        {
            var brandSlug = FirstNonEmpty(Str(body.Value, "brandSlug"), Str(body.Value, "websiteSlug"));
            if (brandSlug.Length > 0)
            {
                brand = await db.Brands.FirstOrDefaultAsync(b => b.Slug == brandSlug, ct);
                if (brand is null) return Fail(422, "unknown brandSlug");
            }
        }

        var now = DateTime.UtcNow;
        var uaHint = TicketReporter.FromUserAgent(FirstNonEmpty(Str(body.Value, "userAgent"), ctx.Request.Headers["User-Agent"].ToString()));
        var addressObj = Obj(body.Value, "address");
        var street = TicketReporter.Clip(FirstNonEmpty(
            Str(body.Value, "street"),
            Str(body.Value, "streetAddress"),
            Str(body.Value, "addressLine1"),
            Str(addressObj, "street"),
            Str(addressObj, "streetAddress"),
            Str(addressObj, "line1")), 200);
        var city = TicketReporter.Clip(FirstNonEmpty(Str(body.Value, "city"), Str(addressObj, "city")), 100);
        var region = TicketReporter.Clip(FirstNonEmpty(
            Str(body.Value, "state"),
            Str(body.Value, "province"),
            Str(body.Value, "region"),
            Str(addressObj, "state"),
            Str(addressObj, "province"),
            Str(addressObj, "region")), 100);
        var postal = TicketReporter.Clip(FirstNonEmpty(
            Str(body.Value, "postalCode"),
            Str(body.Value, "postal"),
            Str(body.Value, "zip"),
            Str(body.Value, "zipCode"),
            Str(body.Value, "postcode"),
            Str(addressObj, "postalCode"),
            Str(addressObj, "zip"),
            Str(addressObj, "zipCode")), 20);
        var country = TicketReporter.Clip(FirstNonEmpty(Str(body.Value, "country"), Str(addressObj, "country")), 100);
        var address = TicketReporter.Clip(TicketReporter.ComposeAddress(
            Str(body.Value, "address"),
            street, city, region, postal, country), 512);
        var ticket = new Ticket
        {
            PublicId = await TicketNumber.NextAsync(db, now, ct),
            CustomerId = customer.Id,
            Customer = customer,
            ProductId = product?.Id,
            BrandId = brand?.Id,
            LicenseId = licenseId,
            Subject = subject,
            Status = TicketStatuses.Open,
            Priority = TicketRules.IsPriority(Str(body.Value, "priority")) ? Str(body.Value, "priority") : TicketPriorities.Normal,
            CreatedAt = now,
            UpdatedAt = now,
            Phone = TicketReporter.Clip(FirstNonEmpty(Str(body.Value, "phone"), Str(body.Value, "phoneNumber"), customer.Phone), 32),
            Street = street,
            City = city,
            Region = region,
            PostalCode = postal,
            Country = country,
            Address = address,
            IpAddress = TicketReporter.ClientIp(ctx, FirstNonEmpty(Str(body.Value, "ip"), Str(body.Value, "ipAddress"))),
            Device = TicketReporter.Clip(FirstNonEmpty(Str(body.Value, "device"), uaHint.Device), 32),
            OsVersion = TicketReporter.Clip(FirstNonEmpty(Str(body.Value, "os"), Str(body.Value, "osVersion"), uaHint.Os), 128),
            Browser = TicketReporter.Clip(FirstNonEmpty(Str(body.Value, "browser"), uaHint.Browser), 64),
            Language = TicketReporter.Clip(FirstNonEmpty(Str(body.Value, "language"), Str(body.Value, "locale"), Str(body.Value, "lang")), 32),
            Timezone = TicketReporter.Clip(FirstNonEmpty(Str(body.Value, "timezone"), Str(body.Value, "timeZone")), 64),
            Screen = TicketReporter.Clip(FirstNonEmpty(Str(body.Value, "screen"), Str(body.Value, "screenSize")), 32),
            Geo = TicketReporter.Clip(TicketReporter.ComposeGeo(Str(body.Value, "geo"), city, region, country), 200),
            Isp = TicketReporter.Clip(Str(body.Value, "isp"), 200)
        };
        ticket.Messages.Add(new TicketMessage
        {
            Author = TicketAuthors.Customer,
            AuthorName = customer.DisplayName.Length > 0 ? customer.DisplayName : email,
            Body = text,
            CreatedAt = DateTime.UtcNow
        });
        db.Tickets.Add(ticket);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
                && pg.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                if (attempt == 4) throw;
                ticket.PublicId = await TicketNumber.NextAsync(db, now, ct);
            }
        }
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

        var q = db.Tickets.AsNoTracking().AsQueryable();
        var status = ctx.Request.Query["status"].ToString().Trim().ToLowerInvariant();
        if (TicketRules.IsStatus(status)) q = q.Where(t => t.Status == status);
        var priority = ctx.Request.Query["priority"].ToString().Trim().ToLowerInvariant();
        if (TicketRules.IsPriority(priority)) q = q.Where(t => t.Priority == priority);
        if (int.TryParse(ctx.Request.Query["customerId"], out var cid) && cid > 0)
            q = q.Where(t => t.CustomerId == cid);

        var search = ctx.Request.Query["q"].ToString().Trim();
        if (search.Length > 0)
        {
            var s = search.ToLowerInvariant();
            q = q.Where(t =>
                t.PublicId.ToLower().Contains(s)
                || t.Subject.ToLower().Contains(s)
                || t.Customer.Email.ToLower().Contains(s)
                || t.Customer.DisplayName.ToLower().Contains(s)
                || t.Phone.ToLower().Contains(s)
                || t.Street.ToLower().Contains(s)
                || t.City.ToLower().Contains(s)
                || t.Region.ToLower().Contains(s)
                || t.PostalCode.ToLower().Contains(s)
                || t.Country.ToLower().Contains(s)
                || t.Address.ToLower().Contains(s)
                || t.IpAddress.Contains(s)
                || t.Geo.ToLower().Contains(s));
        }

        if (DateOnly.TryParse(ctx.Request.Query["from"], out var fromDay))
        {
            var fromUtc = fromDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(t => t.UpdatedAt >= fromUtc);
        }
        if (DateOnly.TryParse(ctx.Request.Query["to"], out var toDay))
        {
            var toExclusive = toDay.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(t => t.UpdatedAt < toExclusive);
        }

        var projected = q.Select(t => new
        {
            t.Id, t.PublicId, t.CustomerId, t.ProductId, t.BrandId, t.LicenseId, t.Subject, t.Status, t.Priority,
            t.CreatedAt, t.UpdatedAt,
            CustomerEmail = t.Customer.Email,
            CustomerName = t.Customer.DisplayName,
            ProductName = t.ProductId == null
                ? ""
                : db.Products.Where(p => p.Id == t.ProductId).Select(p => p.DisplayName).FirstOrDefault() ?? "",
            WebsiteName = t.BrandId != null
                ? db.Brands.Where(b => b.Id == t.BrandId).Select(b => b.Name).FirstOrDefault() ?? ""
                : db.Products.Where(p => p.Id == t.ProductId && p.BrandId != null)
                    .Select(p => p.Brand!.Name).FirstOrDefault() ?? "",
            MessageCount = t.Messages.Count
        });

        var sort = ctx.Request.Query["sort"].ToString().Trim().ToLowerInvariant();
        var asc = ctx.Request.Query["dir"].ToString().Trim().Equals("asc", StringComparison.OrdinalIgnoreCase);
        projected = sort switch
        {
            "id" or "publicid" or "ticket" => asc
                ? projected.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
                : projected.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id),
            "subject" => asc ? projected.OrderBy(x => x.Subject) : projected.OrderByDescending(x => x.Subject),
            "email" or "customer" => asc
                ? projected.OrderBy(x => x.CustomerEmail)
                : projected.OrderByDescending(x => x.CustomerEmail),
            "product" => asc ? projected.OrderBy(x => x.ProductName) : projected.OrderByDescending(x => x.ProductName),
            "website" or "brand" => asc ? projected.OrderBy(x => x.WebsiteName) : projected.OrderByDescending(x => x.WebsiteName),
            "status" => asc ? projected.OrderBy(x => x.Status) : projected.OrderByDescending(x => x.Status),
            "priority" => asc ? projected.OrderBy(x => x.Priority) : projected.OrderByDescending(x => x.Priority),
            "createdat" => asc ? projected.OrderBy(x => x.CreatedAt) : projected.OrderByDescending(x => x.CreatedAt),
            _ => asc ? projected.OrderBy(x => x.UpdatedAt) : projected.OrderByDescending(x => x.UpdatedAt)
        };

        var total = await projected.CountAsync(ct);
        var limit = CatalogPaging.NormalizeLimit(ctx.Request.Query["limit"].ToString());
        var page = CatalogPaging.ClampPage(CatalogPaging.NormalizePage(ctx.Request.Query["page"].ToString()), total, limit);
        var rows = await projected.Skip((page - 1) * limit).Take(limit).ToListAsync(ct);

        var allCount = await db.Tickets.CountAsync(ct);
        var openCount = await db.Tickets.CountAsync(t => t.Status == TicketStatuses.Open, ct);
        var waitingCount = await db.Tickets.CountAsync(t => t.Status == TicketStatuses.Waiting, ct);
        var closedCount = await db.Tickets.CountAsync(t => t.Status == TicketStatuses.Closed, ct);

        return Json(200, new
        {
            tickets = rows.Select(x => new
            {
                x.Id, x.PublicId, x.CustomerId, customerEmail = x.CustomerEmail, customerName = x.CustomerName,
                x.ProductId, productName = x.ProductName, x.BrandId, websiteName = x.WebsiteName, x.LicenseId,
                x.Subject, x.Status, x.Priority,
                messageCount = x.MessageCount,
                createdAt = x.CreatedAt.ToString("o"),
                updatedAt = x.UpdatedAt.ToString("o")
            }),
            total,
            page,
            limit,
            counts = new { total = allCount, open = openCount, waiting = waitingCount, closed = closedCount }
        });
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
        return Json(200, new { ticket = await AdminDetailAsync(db, ticket, ct) });
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
        return Json(200, new { ticket = await AdminDetailAsync(db, ticket, ct) });
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
        return Json(200, new { ticket = await AdminDetailAsync(db, ticket, ct) });
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
        return Json(200, new { ticket = await AdminDetailAsync(db, ticket, ct) });
    }

    private static async Task<Ticket?> FindTicket(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return null;
        if (TryInt(body.Value, "id", out var id))
            return await db.Tickets.Include(t => t.Customer).Include(t => t.Messages)
                .FirstOrDefaultAsync(t => t.Id == id, ct);
        return null;
    }

    private static async Task<Ticket?> LoadByPublicId(AppDb db, string publicId, CancellationToken ct)
    {
        var id = TicketNumber.Normalize(publicId);
        if (id.Length == 0) return null;
        return await db.Tickets.Include(t => t.Customer).Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.PublicId == id, ct);
    }

    private static object PublicView(Ticket t, bool includeMessages) => new
    {
        publicId = t.PublicId,
        t.Subject, t.Status, t.Priority,
        createdAt = t.CreatedAt.ToString("o"),
        updatedAt = t.UpdatedAt.ToString("o"),
        messages = includeMessages ? t.Messages.OrderBy(m => m.Id).Select(MessageView) : null
    };

    private static async Task<object> AdminDetailAsync(AppDb db, Ticket t, CancellationToken ct)
    {
        var product = t.ProductId is int pid
            ? await db.Products.AsNoTracking().Include(p => p.Brand).FirstOrDefaultAsync(p => p.Id == pid, ct)
            : null;
        var brand = t.BrandId is int bid
            ? await db.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bid, ct)
            : product?.Brand;
        string? licenseKey = t.LicenseId is int lid
            ? await db.Licenses.AsNoTracking().Where(l => l.Id == lid).Select(l => l.Key).FirstOrDefaultAsync(ct)
            : null;
        var ordered = t.Messages.OrderBy(m => m.Id).ToList();
        var firstCustomer = ordered.FirstOrDefault(m => m.Author == TicketAuthors.Customer);
        var lastCustomer = ordered.LastOrDefault(m => m.Author == TicketAuthors.Customer);
        var lastStaff = ordered.LastOrDefault(m => m.Author == TicketAuthors.Staff);
        var phone = t.Phone.Length > 0 ? t.Phone : t.Customer?.Phone ?? "";
        return new
        {
            t.Id, t.PublicId, t.CustomerId,
            customerEmail = t.Customer?.Email ?? "",
            customerName = t.Customer?.DisplayName ?? "",
            phone,
            street = t.Street,
            city = t.City,
            region = t.Region,
            postalCode = t.PostalCode,
            country = t.Country,
            address = t.Address.Length > 0 ? t.Address : TicketReporter.ComposeAddress("", t.Street, t.City, t.Region, t.PostalCode, t.Country),
            t.ProductId, productName = product?.DisplayName ?? "",
            brandId = brand?.Id,
            websiteName = brand?.Name ?? "",
            websiteUrl = brand?.WebsiteUrl ?? "",
            t.LicenseId, licenseKey,
            t.Subject, t.Status, t.Priority,
            ip = t.IpAddress,
            device = TicketReporter.DeviceLabel(t.Device, t.OsVersion, t.Browser),
            language = t.Language.Length > 0 ? t.Language : t.Customer?.Locale ?? "",
            timezone = t.Timezone,
            screen = t.Screen,
            geo = t.Geo,
            isp = t.Isp,
            mail = lastStaff is not null ? "sent" : "pending",
            adminEmailedAt = lastStaff?.CreatedAt.ToString("o"),
            customerEmailedAt = lastCustomer?.CreatedAt.ToString("o"),
            issue = firstCustomer?.Body ?? "",
            createdAt = t.CreatedAt.ToString("o"),
            updatedAt = t.UpdatedAt.ToString("o"),
            messages = ordered.Select(MessageView)
        };
    }

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

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        return "";
    }

    private static JsonElement Obj(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object
            ? v : default;

    private static string Str(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "").Trim() : "";

    private static bool TryInt(JsonElement body, string name, out int result)
    {
        result = 0;
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(name, out var v)) return false;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt32(out result);
        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out result);
    }

    private static IResult Fail(int status, string error) => Json(status, new { error });
    private static IResult Json(int status, object body) => Results.Json(body, JsonOptions, statusCode: status);
}
