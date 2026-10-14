using System.Text.Json;
using MasterServer.Admin;
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

        var q = db.Orders.AsNoTracking().AsQueryable();
        var status = ctx.Request.Query["status"].ToString().Trim().ToLowerInvariant();
        if (status.Length > 0) q = q.Where(o => o.Status == status);
        if (int.TryParse(ctx.Request.Query["productId"], out var pid)) q = q.Where(o => o.ProductId == pid);
        var search = ctx.Request.Query["q"].ToString().Trim();
        if (search.Length > 0)
            q = q.Where(o => o.PublicId == search || o.IdempotencyKey == search);

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

    private static async Task<IResult> ListMaps(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.PaymentRead, ct);
        if (g.Result is not null) return g.Result;
        var rows = await db.GatewayPlans.AsNoTracking().OrderBy(m => m.Id).ToListAsync(ct);
        return Json(200, new { gatewayPlans = rows.Select(MapView) });
    }

    private static async Task<IResult> CreateMap(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.PaymentWrite, ct);
        if (g.Result is not null) return g.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        var gateway = Str(body.Value, "gateway").ToLowerInvariant();
        var planCode = Str(body.Value, "planCode").ToLowerInvariant();
        var externalPlanId = Str(body.Value, "externalPlanId");
        if (gateway.Length == 0 || planCode.Length == 0 || externalPlanId.Length == 0)
            return Fail(400, "missing gateway, planCode, or externalPlanId");
        if (!TryInt(body.Value, "productId", out var productId))
            return Fail(400, "missing productId");
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct))
            return Fail(422, "unknown productId");
        if (await db.GatewayPlans.AnyAsync(m => m.Gateway == gateway && m.ProductId == productId && m.PlanCode == planCode, ct))
            return Fail(409, "mapping already exists");

        var row = new GatewayPlan
        {
            Gateway = gateway,
            ProductId = productId,
            PlanCode = planCode,
            ExternalPlanId = externalPlanId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.GatewayPlans.Add(row);
        Audit.Record(db, ctx, g.Principal, "gateway-plan.create", $"{gateway}:{planCode}");
        await db.SaveChangesAsync(ct);
        return Json(201, new { gatewayPlan = MapView(row) });
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
        o.Id, o.PublicId, o.CustomerId, o.ProductId, o.PlanType, amount = o.Amount, o.Currency,
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

    private static object MapView(GatewayPlan m) => new
    {
        m.Id, m.Gateway, m.ProductId, m.PlanCode, m.ExternalPlanId, m.IsActive,
        createdAt = m.CreatedAt.ToString("o")
    };

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
