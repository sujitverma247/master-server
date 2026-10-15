using System.Text.Json;
using MasterServer.Admin;
using MasterServer.Catalog;
using MasterServer.Customers;
using MasterServer.Data;
using MasterServer.Mail;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MasterServer.Payments;

public static class PaymentsApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Map(WebApplication app, GatewayRegistry gateways, IMailSender mail)
    {
        app.MapPost("/v1/checkout", (HttpContext c, AppDb db, CancellationToken ct) => Create(c, db, gateways, ct));
        app.MapGet("/v1/checkout/{publicId}", (string publicId, HttpContext c, AppDb db, CancellationToken ct) => GetPublic(publicId, db, ct));
        app.MapPost("/v1/webhooks/{gateway}", (string gateway, HttpContext c, AppDb db, CancellationToken ct) => Webhook(gateway, c, db, gateways, mail, ct));

        app.MapGet("/admin/orders", (HttpContext c, AppDb db, CancellationToken ct) => List(c, db, ct));
        app.MapGet("/admin/orders/{id:int}", (int id, HttpContext c, AppDb db, CancellationToken ct) => Get(id, c, db, ct));
        app.MapPost("/admin/orders/mark-paid", (HttpContext c, AppDb db, CancellationToken ct) => MarkPaid(c, db, mail, ct));
        app.MapPost("/admin/orders/refund", (HttpContext c, AppDb db, CancellationToken ct) => Refund(c, db, mail, ct));

        app.MapGet("/admin/gateway-plans", (HttpContext c, AppDb db, CancellationToken ct) => ListMaps(c, db, ct));
        app.MapPost("/admin/gateway-plans", (HttpContext c, AppDb db, CancellationToken ct) => CreateMap(c, db, ct));
        app.MapPut("/admin/gateway-plans/{id:int}", (int id, HttpContext c, AppDb db, CancellationToken ct) => UpdateMap(id, c, db, ct));
        app.MapDelete("/admin/gateway-plans/{id:int}", (int id, HttpContext c, AppDb db, CancellationToken ct) => DeleteMap(id, c, db, ct));
    }

    // ---------- Public checkout ----------

    private static async Task<IResult> Create(HttpContext ctx, AppDb db, GatewayRegistry gateways, CancellationToken ct)
    {
        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        if (!EmailAddress.TryNormalize(Str(body.Value, "email"), out var email))
            return Fail(400, "invalid email");
        if (!TryInt(body.Value, "productId", out var productId))
            return Fail(400, "missing productId");

        var planType = Str(body.Value, "planType").ToLowerInvariant();
        if (planType.Length == 0) return Fail(400, "missing planType");

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.IsActive, ct);
        if (product is null) return Fail(422, "unknown productId");
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.IsActive && p.Code == planType, ct);
        if (plan is null) return Fail(422, "unknown planType");

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Email == email, ct);
        if (customer is null || !customer.IsActive)
            return Fail(422, "accept the current refund policy");
        if (!await RefundPolicyGate.HasAcceptedCurrentAsync(db, customer.Id, ct))
            return Fail(422, "accept the current refund policy");

        var gateway = gateways.Resolve(Str(body.Value, "gateway"));
        if (gateway is null) return Fail(422, "unknown gateway");

        string? externalPlanId = null;
        if (gateway.RequiresExternalPlan)
        {
            var map = await db.GatewayPlans.AsNoTracking().FirstOrDefaultAsync(m =>
                m.IsActive && m.Gateway == gateway.Name && m.ProductId == productId && m.PlanCode == planType, ct);
            if (map is null) return Fail(422, "no gateway plan mapping");
            externalPlanId = map.ExternalPlanId;
        }

        var rawKey = Str(body.Value, "idempotencyKey");
        string idem;
        if (rawKey.Length == 0) idem = IdempotencyKey.New();
        else if (!IdempotencyKey.TryNormalize(rawKey, out idem))
            return Fail(400, "invalid idempotencyKey");

        var existing = await db.Orders.FirstOrDefaultAsync(o => o.IdempotencyKey == idem, ct);
        if (existing is not null)
        {
            if (existing.CustomerId != customer.Id || existing.ProductId != productId || existing.PlanType != planType)
                return Fail(409, "idempotency key reuse mismatch");
            return Json(200, PublicView(existing, includeKey: false));
        }

        var currentPolicy = await RefundPolicyGate.CurrentAsync(db, ct);
        var publicId = Guid.NewGuid().ToString("N");
        var start = gateway.Start(new OrderStartRequest(publicId, email, planType, Money.Normalize(plan.Price), plan.Currency, externalPlanId));

        var order = new Order
        {
            PublicId = publicId,
            IdempotencyKey = idem,
            CustomerId = customer.Id,
            ProductId = productId,
            PlanType = planType,
            Amount = Money.Normalize(plan.Price),
            Currency = plan.Currency,
            Gateway = gateway.Name,
            Status = OrderStatuses.Pending,
            RefundPolicyId = currentPolicy?.Id,
            CheckoutUrl = start.CheckoutUrl,
            ExternalCheckoutId = start.ExternalCheckoutId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Orders.Add(order);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            var raced = await db.Orders.FirstOrDefaultAsync(o => o.IdempotencyKey == idem, ct);
            if (raced is not null) return Json(200, PublicView(raced, includeKey: false));
            return Fail(409, "idempotency key reuse mismatch");
        }

        Audit.Record(db, ctx, null, "order.create", $"order#{order.Id}", $"gateway={gateway.Name},plan={planType}");
        await db.SaveChangesAsync(ct);
        return Json(201, PublicView(order, includeKey: false));
    }

    private static async Task<IResult> GetPublic(string publicId, AppDb db, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.License)
            .FirstOrDefaultAsync(o => o.PublicId == publicId, ct);
        if (order is null) return Fail(404, "order not found");
        return Json(200, PublicView(order, includeKey: order.Status == OrderStatuses.Paid));
    }

    private static async Task<IResult> Webhook(string gatewayName, HttpContext ctx, AppDb db, GatewayRegistry gateways, IMailSender mail, CancellationToken ct)
    {
        var gateway = gateways.Resolve(gatewayName);
        if (gateway is null) return Fail(404, "unknown gateway");

        using var reader = new StreamReader(ctx.Request.Body);
        var raw = await reader.ReadToEndAsync(ct);
        if (!gateway.TryVerify(ctx.Request, raw, out var envelope))
            return Fail(401, "unauthorized");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.FirstOrDefaultAsync(o => o.PublicId == envelope.PublicId && o.Gateway == gateway.Name, ct);
        if (order is null) return Fail(404, "order not found");

        db.PaymentEvents.Add(new PaymentEvent
        {
            Gateway = gateway.Name,
            ExternalEventId = envelope.EventId,
            EventType = envelope.EventType,
            OrderId = order.Id,
            PayloadSha256 = IdempotencyKey.Sha256Hex(raw),
            CreatedAt = DateTime.UtcNow
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(ct);
            return Json(200, new { received = true, duplicate = true });
        }

        var error = await OrderFulfillment.ApplyAsync(db, order, envelope.Status, envelope.ExternalPaymentId, ct);
        if (error is not null)
        {
            await tx.RollbackAsync(ct);
            return Fail(422, error);
        }

        Audit.Record(db, ctx, null, "order.webhook", $"order#{order.Id}", $"gateway={gateway.Name},status={order.Status}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await MailDispatch.DrainAsync(db, mail, 20, ct);
        return Json(200, new { received = true, duplicate = false, status = order.Status });
    }

    // ---------- Admin orders ----------

    private static async Task<IResult> List(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.PaymentRead, ct);
        if (g.Result is not null) return g.Result;

        var q = db.Orders.AsNoTracking().Include(o => o.Customer).AsQueryable();
        var status = ctx.Request.Query["status"].ToString().Trim().ToLowerInvariant();
        if (status.Length > 0) q = q.Where(o => o.Status == status);
        if (int.TryParse(ctx.Request.Query["productId"], out var pid)) q = q.Where(o => o.ProductId == pid);
        var search = ctx.Request.Query["q"].ToString().Trim();
        if (search.Length > 0)
        {
            var s = search.ToLowerInvariant();
            q = q.Where(o =>
                o.PublicId.ToLower().Contains(s) ||
                o.IdempotencyKey.ToLower().Contains(s) ||
                (o.Customer != null && o.Customer.Email.ToLower().Contains(s)));
        }

        var limit = int.TryParse(ctx.Request.Query["limit"], out var lim) ? Math.Clamp(lim, 1, 1000) : 100;
        var rows = await q.OrderByDescending(o => o.Id).Take(limit).ToListAsync(ct);
        return Json(200, new { orders = rows.Select(AdminSummary) });
    }

    private static async Task<IResult> Get(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.PaymentRead, ct);
        if (g.Result is not null) return g.Result;

        var order = await db.Orders.AsNoTracking()
            .Include(o => o.License)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return Fail(404, "order not found");
        return Json(200, new { order = AdminDetail(order) });
    }

    private static async Task<IResult> MarkPaid(HttpContext ctx, AppDb db, IMailSender mail, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.PaymentWrite, ct);
        if (g.Result is not null) return g.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        var order = await FindOrder(db, body.Value, ct);
        if (order is null) return Fail(404, "order not found");
        if (order.Status == OrderStatuses.Refunded) return Fail(422, "order refunded");

        var eventId = $"manual:{order.PublicId}:{DateTime.UtcNow:yyyyMMddHHmmss}";
        db.PaymentEvents.Add(new PaymentEvent
        {
            Gateway = order.Gateway,
            ExternalEventId = eventId,
            EventType = "mark-paid",
            OrderId = order.Id,
            PayloadSha256 = "",
            CreatedAt = DateTime.UtcNow
        });

        var error = await OrderFulfillment.ApplyAsync(db, order, OrderStatuses.Paid, Str(body.Value, "externalPaymentId"), ct);
        if (error is not null) return Fail(422, error);

        Audit.Record(db, ctx, g.Principal, "order.mark-paid", $"order#{order.Id}", MaskKey(order.License?.Key));
        await db.SaveChangesAsync(ct);
        await MailDispatch.DrainAsync(db, mail, 20, ct);
        await db.Entry(order).Reference(o => o.License).LoadAsync(ct);
        return Json(200, new { order = AdminDetail(order) });
    }

    private static async Task<IResult> Refund(HttpContext ctx, AppDb db, IMailSender mail, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.PaymentWrite, ct);
        if (g.Result is not null) return g.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        var order = await FindOrder(db, body.Value, ct);
        if (order is null) return Fail(404, "order not found");

        var error = await OrderFulfillment.ApplyAsync(db, order, OrderStatuses.Refunded, null, ct);
        if (error is not null) return Fail(422, error);

        Audit.Record(db, ctx, g.Principal, "order.refund", $"order#{order.Id}");
        await db.SaveChangesAsync(ct);
        await MailDispatch.DrainAsync(db, mail, 20, ct);
        return Json(200, new { order = AdminSummary(order) });
    }

    // ---------- Gateway plan maps ----------
    // Price lives on the payment gateway. These rows map (product + plan code) to the
    // gateway's plan/product id, the same way the existing license server mapped Freemius
    // plan ids (e.g. 68452 → 1year) without storing a local catalog price.

    private static async Task<IResult> ListMaps(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await RequireAny(ctx, db, ct, Permissions.ProductRead, Permissions.PaymentRead);
        if (g.Result is not null) return g.Result;

        var q = db.GatewayPlans.AsNoTracking().AsQueryable();
        var search = ctx.Request.Query["q"].ToString().Trim().ToLowerInvariant();
        if (search.Length > 0)
        {
            q = q.Where(m =>
                m.PlanCode.Contains(search)
                || m.Gateway.Contains(search)
                || m.ExternalPlanId.ToLower().Contains(search)
                || db.Products.Any(p => p.Id == m.ProductId && (p.DisplayName.ToLower().Contains(search) || p.Slug.Contains(search))));
        }

        var status = ctx.Request.Query["status"].ToString().Trim().ToLowerInvariant();
        if (status == "active") q = q.Where(m => m.IsActive);
        else if (status == "inactive") q = q.Where(m => !m.IsActive);

        if (int.TryParse(ctx.Request.Query["productId"], out var productId) && productId > 0)
            q = q.Where(m => m.ProductId == productId);

        var gatewayFilter = ctx.Request.Query["gateway"].ToString().Trim().ToLowerInvariant();
        if (gatewayFilter.Length > 0) q = q.Where(m => m.Gateway == gatewayFilter);

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
            Map = m,
            ProductName = db.Products.Where(p => p.Id == m.ProductId).Select(p => p.DisplayName).FirstOrDefault() ?? "",
            ProductSlug = db.Products.Where(p => p.Id == m.ProductId).Select(p => p.Slug).FirstOrDefault() ?? "",
            DisplayName = db.Plans.Where(p => p.Code == m.PlanCode).Select(p => p.DisplayName).FirstOrDefault() ?? m.PlanCode,
            DurationDays = db.Plans.Where(p => p.Code == m.PlanCode).Select(p => (int?)p.DurationDays).FirstOrDefault() ?? 0,
            DefaultMaxMachines = db.Plans.Where(p => p.Code == m.PlanCode).Select(p => (int?)p.DefaultMaxMachines).FirstOrDefault() ?? 3,
            LicenseCount = db.Licenses.Count(l => l.ProductId == m.ProductId && l.PlanType.ToLower() == m.PlanCode)
        });

        var sort = ctx.Request.Query["sort"].ToString().Trim().ToLowerInvariant();
        var asc = ctx.Request.Query["dir"].ToString().Trim().Equals("asc", StringComparison.OrdinalIgnoreCase);
        projected = sort switch
        {
            "product" => asc ? projected.OrderBy(x => x.ProductName) : projected.OrderByDescending(x => x.ProductName),
            "name" => asc ? projected.OrderBy(x => x.DisplayName) : projected.OrderByDescending(x => x.DisplayName),
            "code" => asc ? projected.OrderBy(x => x.Map.PlanCode) : projected.OrderByDescending(x => x.Map.PlanCode),
            "gateway" => asc ? projected.OrderBy(x => x.Map.Gateway) : projected.OrderByDescending(x => x.Map.Gateway),
            "external" => asc ? projected.OrderBy(x => x.Map.ExternalPlanId) : projected.OrderByDescending(x => x.Map.ExternalPlanId),
            "duration" => asc ? projected.OrderBy(x => x.DurationDays) : projected.OrderByDescending(x => x.DurationDays),
            "machines" => asc ? projected.OrderBy(x => x.DefaultMaxMachines) : projected.OrderByDescending(x => x.DefaultMaxMachines),
            "status" => asc ? projected.OrderBy(x => x.Map.IsActive) : projected.OrderByDescending(x => x.Map.IsActive),
            _ => asc ? projected.OrderBy(x => x.Map.CreatedAt) : projected.OrderByDescending(x => x.Map.CreatedAt)
        };

        var total = await projected.CountAsync(ct);
        var limit = CatalogPaging.NormalizeLimit(ctx.Request.Query["limit"].ToString());
        var page = CatalogPaging.ClampPage(CatalogPaging.NormalizePage(ctx.Request.Query["page"].ToString()), total, limit);
        var rows = await projected.Skip((page - 1) * limit).Take(limit).ToListAsync(ct);

        var allCount = await db.GatewayPlans.CountAsync(ct);
        var activeCount = await db.GatewayPlans.CountAsync(m => m.IsActive, ct);
        var gateways = await db.GatewayPlans.Select(m => m.Gateway).Distinct().OrderBy(n => n).ToListAsync(ct);

        return Json(200, new
        {
            plans = rows.Select(x => MapView(x.Map, x.ProductName, x.ProductSlug, x.DisplayName, x.DurationDays, x.DefaultMaxMachines, x.LicenseCount)),
            gatewayPlans = rows.Select(x => MapView(x.Map, x.ProductName, x.ProductSlug, x.DisplayName, x.DurationDays, x.DefaultMaxMachines, x.LicenseCount)),
            total,
            page,
            limit,
            counts = new { total = allCount, active = activeCount, inactive = allCount - activeCount },
            gateways
        });
    }

    private static async Task<IResult> CreateMap(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await RequireAny(ctx, db, ct, Permissions.PlanWrite, Permissions.PaymentWrite);
        if (g.Result is not null) return g.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        if (!TryReadMapping(body.Value, out var mapping, out var error))
            return Fail(400, error);

        if (!await db.Products.AnyAsync(p => p.Id == mapping.ProductId, ct))
            return Fail(422, "unknown productId");
        if (await db.GatewayPlans.AnyAsync(m => m.Gateway == mapping.Gateway && m.ProductId == mapping.ProductId && m.PlanCode == mapping.PlanCode, ct))
            return Fail(409, "mapping already exists");

        await UpsertPlanDurationAsync(db, mapping, ct);

        var row = new GatewayPlan
        {
            Gateway = mapping.Gateway,
            ProductId = mapping.ProductId,
            PlanCode = mapping.PlanCode,
            ExternalPlanId = mapping.ExternalPlanId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.GatewayPlans.Add(row);
        Audit.Record(db, ctx, g.Principal, "gateway-plan.create", $"{mapping.Gateway}:{mapping.PlanCode}", $"product={mapping.ProductId},external={mapping.ExternalPlanId}");
        await db.SaveChangesAsync(ct);
        return Json(201, new { plan = await MapViewAsync(db, row, ct), gatewayPlan = await MapViewAsync(db, row, ct) });
    }

    private static async Task<IResult> UpdateMap(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await RequireAny(ctx, db, ct, Permissions.PlanWrite, Permissions.PaymentWrite);
        if (g.Result is not null) return g.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");

        var row = await db.GatewayPlans.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (row is null) return Fail(404, "plan not found");

        if (!TryReadMapping(body.Value, out var mapping, out var error, requireAll: false))
            return Fail(400, error);

        if (mapping.ProductId > 0)
        {
            if (!await db.Products.AnyAsync(p => p.Id == mapping.ProductId, ct))
                return Fail(422, "unknown productId");
            row.ProductId = mapping.ProductId;
        }
        if (mapping.Gateway.Length > 0) row.Gateway = mapping.Gateway;
        if (mapping.PlanCode.Length > 0) row.PlanCode = mapping.PlanCode;
        if (mapping.ExternalPlanId.Length > 0) row.ExternalPlanId = mapping.ExternalPlanId;
        if (TryBool(body.Value, "isActive", out var active)) row.IsActive = active;

        if (await db.GatewayPlans.AnyAsync(m => m.Id != id && m.Gateway == row.Gateway && m.ProductId == row.ProductId && m.PlanCode == row.PlanCode, ct))
            return Fail(409, "mapping already exists");

        mapping = mapping with { ProductId = row.ProductId, Gateway = row.Gateway, PlanCode = row.PlanCode, ExternalPlanId = row.ExternalPlanId };
        await UpsertPlanDurationAsync(db, mapping, ct);

        Audit.Record(db, ctx, g.Principal, "gateway-plan.update", $"{row.Gateway}:{row.PlanCode}", $"external={row.ExternalPlanId}");
        await db.SaveChangesAsync(ct);
        return Json(200, new { plan = await MapViewAsync(db, row, ct), gatewayPlan = await MapViewAsync(db, row, ct) });
    }

    private static async Task<IResult> DeleteMap(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await RequireAny(ctx, db, ct, Permissions.PlanWrite, Permissions.PaymentWrite);
        if (g.Result is not null) return g.Result;

        var row = await db.GatewayPlans.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (row is null) return Fail(404, "plan not found");
        if (await db.Licenses.AnyAsync(l => l.ProductId == row.ProductId && l.PlanType.ToLower() == row.PlanCode, ct))
            return Fail(409, "plan has licenses");

        db.GatewayPlans.Remove(row);
        Audit.Record(db, ctx, g.Principal, "gateway-plan.delete", $"{row.Gateway}:{row.PlanCode}");
        await db.SaveChangesAsync(ct);
        return Json(200, new { ok = true, id });
    }

    // ---------- Helpers ----------

    private static async Task<Order?> FindOrder(AppDb db, JsonElement body, CancellationToken ct)
    {
        if (TryInt(body, "id", out var id))
            return await db.Orders.Include(o => o.License).FirstOrDefaultAsync(o => o.Id == id, ct);
        var publicId = Str(body, "publicId");
        if (publicId.Length > 0)
            return await db.Orders.Include(o => o.License).FirstOrDefaultAsync(o => o.PublicId == publicId, ct);
        return null;
    }

    private static object PublicView(Order o, bool includeKey) => new
    {
        publicId = o.PublicId,
        o.Status,
        o.PlanType,
        amount = o.Amount,
        o.Currency,
        o.Gateway,
        checkoutUrl = o.CheckoutUrl,
        licenseKey = includeKey ? o.License?.Key : null,
        paidAt = o.PaidAt?.ToString("o"),
        createdAt = o.CreatedAt.ToString("o")
    };

    private static object AdminSummary(Order o) => new
    {
        o.Id, o.PublicId, o.CustomerId, customerEmail = o.Customer?.Email ?? "",
        o.ProductId, o.PlanType, amount = o.Amount, o.Currency,
        o.Gateway, o.Status, o.LicenseId,
        paidAt = o.PaidAt?.ToString("o"),
        createdAt = o.CreatedAt.ToString("o")
    };

    private static object AdminDetail(Order o) => new
    {
        o.Id, o.PublicId, o.IdempotencyKey, o.CustomerId,
        customerEmail = o.Customer?.Email,
        o.ProductId, o.PlanType, amount = o.Amount, o.Currency,
        o.Gateway, o.Status, o.LicenseId,
        licenseKey = o.License?.Key,
        o.ExternalPaymentId, o.ExternalCheckoutId, o.RefundPolicyId,
        paidAt = o.PaidAt?.ToString("o"),
        createdAt = o.CreatedAt.ToString("o")
    };

    private readonly record struct MappingInput(
        int ProductId,
        string Gateway,
        string PlanCode,
        string ExternalPlanId,
        string DisplayName,
        int DurationDays,
        int DefaultMaxMachines);

    private static bool TryReadMapping(JsonElement body, out MappingInput mapping, out string error, bool requireAll = true)
    {
        mapping = default;
        error = "";
        var gateway = Str(body, "gateway").ToLowerInvariant();
        var planCode = FirstNonEmpty(Str(body, "planCode"), Str(body, "code")).ToLowerInvariant();
        var externalPlanId = FirstNonEmpty(Str(body, "externalPlanId"), Str(body, "gatewayPlanId"));
        TryInt(body, "productId", out var productId);
        var displayName = Str(body, "displayName");
        var durationDays = TryInt(body, "durationDays", out var dd) ? dd : 0;
        var maxMachines = TryInt(body, "defaultMaxMachines", out var mm) ? Math.Clamp(mm, 1, 100) : 0;

        if (requireAll)
        {
            if (productId <= 0) { error = "missing productId"; return false; }
            if (gateway.Length == 0) { error = "missing gateway"; return false; }
            if (!IsPlanCode(planCode)) { error = "code must be 2-32 chars of a-z, 0-9, hyphen"; return false; }
            if (externalPlanId.Length is < 1 or > 128) { error = "missing externalPlanId"; return false; }
            if (displayName.Length is < 2 or > 200) { error = "displayName too short"; return false; }
            if (durationDays is < 1 or > 36500) { error = "durationDays must be 1-36500"; return false; }
            if (maxMachines == 0) maxMachines = 3;
        }
        else
        {
            if (gateway.Length > 32) { error = "missing gateway"; return false; }
            if (planCode.Length > 0 && !IsPlanCode(planCode)) { error = "code must be 2-32 chars of a-z, 0-9, hyphen"; return false; }
            if (externalPlanId.Length > 128) { error = "missing externalPlanId"; return false; }
            if (displayName.Length > 0 && displayName.Length < 2) { error = "displayName too short"; return false; }
            if (durationDays > 0 && durationDays > 36500) { error = "durationDays must be 1-36500"; return false; }
        }

        mapping = new MappingInput(productId, gateway, planCode, externalPlanId, displayName, durationDays, maxMachines);
        return true;
    }

    private static async Task UpsertPlanDurationAsync(AppDb db, MappingInput mapping, CancellationToken ct)
    {
        if (mapping.PlanCode.Length == 0) return;
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Code == mapping.PlanCode, ct);
        if (plan is null)
        {
            db.Plans.Add(new Plan
            {
                Code = mapping.PlanCode,
                DisplayName = mapping.DisplayName.Length > 0 ? mapping.DisplayName : mapping.PlanCode,
                DurationDays = mapping.DurationDays > 0 ? mapping.DurationDays : 365,
                DefaultMaxMachines = mapping.DefaultMaxMachines,
                Price = 0m,
                Currency = "",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            return;
        }
        if (mapping.DisplayName.Length >= 2) plan.DisplayName = mapping.DisplayName;
        if (mapping.DurationDays is >= 1 and <= 36500) plan.DurationDays = mapping.DurationDays;
        if (mapping.DefaultMaxMachines is >= 1 and <= 100) plan.DefaultMaxMachines = mapping.DefaultMaxMachines;
    }

    private static async Task<object> MapViewAsync(AppDb db, GatewayPlan m, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == m.ProductId, ct);
        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == m.PlanCode, ct);
        var licenseCount = await db.Licenses.CountAsync(l => l.ProductId == m.ProductId && l.PlanType.ToLower() == m.PlanCode, ct);
        return MapView(m, product?.DisplayName ?? "", product?.Slug ?? "", plan?.DisplayName ?? m.PlanCode, plan?.DurationDays ?? 0, plan?.DefaultMaxMachines ?? 3, licenseCount);
    }

    private static object MapView(
        GatewayPlan m,
        string productName = "",
        string productSlug = "",
        string displayName = "",
        int durationDays = 0,
        int defaultMaxMachines = 3,
        int licenseCount = 0) => new
    {
        m.Id,
        m.Gateway,
        m.ProductId,
        productName,
        productSlug,
        planCode = m.PlanCode,
        code = m.PlanCode,
        displayName = displayName.Length > 0 ? displayName : m.PlanCode,
        durationDays,
        defaultMaxMachines,
        m.ExternalPlanId,
        m.IsActive,
        licenseCount,
        createdAt = m.CreatedAt.ToString("o")
    };

    private static async Task<Gate> RequireAny(HttpContext ctx, AppDb db, CancellationToken ct, params string[] permissions)
    {
        var principal = await AdminAuth.ResolveAsync(ctx, db, ct);
        if (principal is null) return new Gate(Fail(401, "unauthorized"), null);
        if (!permissions.Any(principal.Has)) return new Gate(Fail(403, "forbidden"), null);
        return new Gate(null, principal);
    }

    private static bool IsPlanCode(string s) =>
        s.Length is >= 2 and <= 32 && s.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        return "";
    }

    private static bool TryBool(JsonElement body, string name, out bool result)
    {
        result = false;
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(name, out var value))
            return false;
        if (value.ValueKind == JsonValueKind.True) { result = true; return true; }
        if (value.ValueKind == JsonValueKind.False) { result = false; return true; }
        return false;
    }

    private static string MaskKey(string? key) =>
        string.IsNullOrEmpty(key) ? "" : (key.Length <= 4 ? "****" : "****" + key[^4..]);

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
