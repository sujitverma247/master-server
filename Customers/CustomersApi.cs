using System.Text.Json;
using MasterServer.Admin;
using MasterServer.Catalog;
using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Customers;

public static class CustomersApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Map(WebApplication app)
    {
        app.MapGet("/admin/customers", (HttpContext c, AppDb db, CancellationToken ct) => List(c, db, ct));
        app.MapGet("/admin/customers/{id:int}", (int id, HttpContext c, AppDb db, CancellationToken ct) => Get(id, c, db, ct));
        app.MapPost("/admin/customers", (HttpContext c, AppDb db, CancellationToken ct) => Create(c, db, ct));
        app.MapPost("/admin/customers/update", (HttpContext c, AppDb db, CancellationToken ct) => Update(c, db, ct));

        app.MapGet("/admin/refund-policies", (HttpContext c, AppDb db, CancellationToken ct) => ListPolicies(c, db, ct));
        app.MapPost("/admin/refund-policies", (HttpContext c, AppDb db, CancellationToken ct) => CreatePolicy(c, db, ct));
        app.MapPost("/admin/refund-policies/{id:int}/activate", (int id, HttpContext c, AppDb db, CancellationToken ct) => ActivatePolicy(id, c, db, ct));

        app.MapGet("/v1/refund-policy/current", (HttpContext c, AppDb db, CancellationToken ct) => CurrentPolicy(db, ct));
        app.MapGet("/v1/refund-policy/status", (HttpContext c, AppDb db, CancellationToken ct) => Status(c, db, ct));
        app.MapPost("/v1/refund-policy/accept", (HttpContext c, AppDb db, CancellationToken ct) => Accept(c, db, ct));
    }

    // ---------- Customers ----------

    private static async Task<IResult> List(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.CustomerRead, ct);
        if (g.Result is not null) return g.Result;

        var q = db.Customers.AsNoTracking().AsQueryable();
        var search = ctx.Request.Query["q"].ToString().Trim();
        if (search.Length > 0)
        {
            var s = search.ToLowerInvariant();
            q = q.Where(c =>
                c.Email.Contains(s)
                || c.DisplayName.ToLower().Contains(s)
                || c.Phone.Contains(s)
                || c.Notes.ToLower().Contains(s)
                || c.Source.ToLower().Contains(s));
        }

        var status = ctx.Request.Query["status"].ToString().Trim().ToLowerInvariant();
        if (status == "active") q = q.Where(c => c.IsActive);
        else if (status == "inactive") q = q.Where(c => !c.IsActive);

        var source = ctx.Request.Query["source"].ToString().Trim();
        if (source.Length > 0) q = q.Where(c => c.Source == source);

        if (DateOnly.TryParse(ctx.Request.Query["from"], out var fromDay))
        {
            var fromUtc = fromDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(c => c.CreatedAt >= fromUtc);
        }
        if (DateOnly.TryParse(ctx.Request.Query["to"], out var toDay))
        {
            var toExclusive = toDay.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(c => c.CreatedAt < toExclusive);
        }

        var projected = q.Select(c => new
        {
            Customer = c,
            LicenseCount = db.Licenses.Count(l => l.CustomerId == c.Id)
        });

        var sort = ctx.Request.Query["sort"].ToString().Trim().ToLowerInvariant();
        var asc = ctx.Request.Query["dir"].ToString().Trim().Equals("asc", StringComparison.OrdinalIgnoreCase);
        projected = sort switch
        {
            "email" => asc ? projected.OrderBy(x => x.Customer.Email) : projected.OrderByDescending(x => x.Customer.Email),
            "phone" => asc ? projected.OrderBy(x => x.Customer.Phone) : projected.OrderByDescending(x => x.Customer.Phone),
            "name" or "displayname" => asc
                ? projected.OrderBy(x => x.Customer.DisplayName)
                : projected.OrderByDescending(x => x.Customer.DisplayName),
            "source" => asc ? projected.OrderBy(x => x.Customer.Source) : projected.OrderByDescending(x => x.Customer.Source),
            "keys" or "licenses" => asc
                ? projected.OrderBy(x => x.LicenseCount)
                : projected.OrderByDescending(x => x.LicenseCount),
            "status" => asc ? projected.OrderBy(x => x.Customer.IsActive) : projected.OrderByDescending(x => x.Customer.IsActive),
            _ => asc ? projected.OrderBy(x => x.Customer.CreatedAt) : projected.OrderByDescending(x => x.Customer.CreatedAt)
        };

        var total = await projected.CountAsync(ct);
        var limit = CatalogPaging.NormalizeLimit(ctx.Request.Query["limit"].ToString());
        var page = CatalogPaging.ClampPage(CatalogPaging.NormalizePage(ctx.Request.Query["page"].ToString()), total, limit);
        var rows = await projected.Skip((page - 1) * limit).Take(limit).ToListAsync(ct);

        var allCount = await db.Customers.CountAsync(ct);
        var activeCount = await db.Customers.CountAsync(c => c.IsActive, ct);
        var sources = await db.Customers.AsNoTracking()
            .Where(c => c.Source != "")
            .Select(c => c.Source)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync(ct);

        var current = await RefundPolicyGate.CurrentAsync(db, ct);
        HashSet<int> accepted = [];
        if (current is not null && rows.Count > 0)
        {
            var ids = rows.Select(x => x.Customer.Id).ToList();
            accepted = (await db.RefundPolicyAcceptances.AsNoTracking()
                .Where(a => a.PolicyId == current.Id && ids.Contains(a.CustomerId))
                .Select(a => a.CustomerId)
                .ToListAsync(ct)).ToHashSet();
        }

        return Json(200, new
        {
            customers = rows.Select(x => Summary(x.Customer, accepted.Contains(x.Customer.Id), x.LicenseCount)),
            total,
            page,
            limit,
            counts = new { total = allCount, active = activeCount, inactive = allCount - activeCount },
            sources
        });
    }

    private static async Task<IResult> Get(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.CustomerRead, ct);
        if (g.Result is not null) return g.Result;

        var customer = await db.Customers.AsNoTracking()
            .Include(c => c.Licenses)
            .Include(c => c.Acceptances)
            .ThenInclude(a => a.Policy)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return Fail(404, "customer not found");

        var acceptedCurrent = await RefundPolicyGate.HasAcceptedCurrentAsync(db, customer.Id, ct);
        var checkout = await db.CheckoutSessions.AsNoTracking()
            .Where(s => s.CustomerId == customer.Id)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);
        return Json(200, new { customer = Detail(customer, acceptedCurrent, checkout) });
    }

    private static async Task<IResult> Create(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.CustomerWrite, ct);
        if (g.Result is not null) return g.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        if (!EmailAddress.TryNormalize(Str(body.Value, "email"), out var email))
            return Fail(400, "invalid email");
        var displayName = Str(body.Value, "displayName");
        var phone = Str(body.Value, "phone");
        var notes = Str(body.Value, "notes");
        var source = Str(body.Value, "source");
        if (displayName.Length > 200) return Fail(400, "displayName too long");
        if (phone.Length > 32) return Fail(400, "phone too long");
        if (notes.Length > 1024) return Fail(400, "notes too long");
        if (source.Length > 32) return Fail(400, "source too long");
        if (await db.Customers.AnyAsync(c => c.Email == email, ct))
            return Fail(409, "email already exists");

        var customer = new Customer
        {
            Email = email,
            DisplayName = displayName,
            Phone = phone,
            Notes = notes,
            Source = source.Length > 0 ? source : "manual",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        Audit.Record(db, ctx, g.Principal, "customer.create", email);
        await db.SaveChangesAsync(ct);
        return Json(201, new { customer = Summary(customer, false) });
    }

    private static async Task<IResult> Update(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.CustomerWrite, ct);
        if (g.Result is not null) return g.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        if (!TryInt(body.Value, "id", out var id)) return Fail(400, "missing id");

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return Fail(404, "customer not found");

        var changes = new List<string>();
        if (body.Value.TryGetProperty("email", out _))
        {
            if (!EmailAddress.TryNormalize(Str(body.Value, "email"), out var email))
                return Fail(400, "invalid email");
            if (email != customer.Email && await db.Customers.AnyAsync(c => c.Email == email, ct))
                return Fail(409, "email already exists");
            if (email != customer.Email)
            {
                customer.Email = email;
                changes.Add("email");
                var licenses = await db.Licenses.Where(l => l.CustomerId == customer.Id).ToListAsync(ct);
                foreach (var license in licenses) license.CustomerEmail = email;
            }
        }
        if (body.Value.TryGetProperty("displayName", out _))
        {
            var displayName = Str(body.Value, "displayName");
            if (displayName.Length > 200) return Fail(400, "displayName too long");
            customer.DisplayName = displayName;
            changes.Add("displayName");
        }
        if (body.Value.TryGetProperty("phone", out _))
        {
            var phone = Str(body.Value, "phone");
            if (phone.Length > 32) return Fail(400, "phone too long");
            customer.Phone = phone;
            changes.Add("phone");
        }
        if (body.Value.TryGetProperty("notes", out _))
        {
            var notes = Str(body.Value, "notes");
            if (notes.Length > 1024) return Fail(400, "notes too long");
            customer.Notes = notes;
            changes.Add("notes");
        }
        if (TryBool(body.Value, "isActive", out var active)) { customer.IsActive = active; changes.Add($"isActive={active}"); }

        customer.UpdatedAt = DateTime.UtcNow;
        Audit.Record(db, ctx, g.Principal, "customer.update", $"customer#{customer.Id}", string.Join(",", changes));
        await db.SaveChangesAsync(ct);
        var acceptedCurrent = await RefundPolicyGate.HasAcceptedCurrentAsync(db, customer.Id, ct);
        var licenseCount = await db.Licenses.CountAsync(l => l.CustomerId == customer.Id, ct);
        return Json(200, new { customer = Summary(customer, acceptedCurrent, licenseCount) });
    }

    // ---------- Policies (admin) ----------

    private static async Task<IResult> ListPolicies(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.CustomerRead, ct);
        if (g.Result is not null) return g.Result;

        var rows = await db.RefundPolicies.AsNoTracking().OrderByDescending(p => p.Id).ToListAsync(ct);
        return Json(200, new { policies = rows.Select(PolicyView) });
    }

    private static async Task<IResult> CreatePolicy(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.CustomerWrite, ct);
        if (g.Result is not null) return g.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        if (!PolicyVersion.TryNormalize(Str(body.Value, "version"), out var version))
            return Fail(400, "version must be 2-64 chars of a-z, 0-9, dot, hyphen");
        var title = Str(body.Value, "title");
        var policyBody = Str(body.Value, "body");
        if (title.Length == 0) return Fail(400, "missing title");
        if (policyBody.Length == 0) return Fail(400, "missing body");
        if (await db.RefundPolicies.AnyAsync(p => p.Version == version, ct))
            return Fail(409, "version already exists");

        var activate = !TryBool(body.Value, "activate", out var act) || act;
        if (activate)
        {
            await db.RefundPolicies.Where(p => p.IsCurrent)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsCurrent, false), ct);
        }

        var policy = new RefundPolicy
        {
            Version = version,
            Title = title,
            Body = policyBody,
            IsCurrent = activate,
            EffectiveAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.RefundPolicies.Add(policy);
        Audit.Record(db, ctx, g.Principal, "policy.create", version, activate ? "current" : "");
        await db.SaveChangesAsync(ct);
        return Json(201, new { policy = PolicyView(policy) });
    }

    private static async Task<IResult> ActivatePolicy(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.CustomerWrite, ct);
        if (g.Result is not null) return g.Result;

        var policy = await db.RefundPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null) return Fail(404, "policy not found");

        await db.RefundPolicies.Where(p => p.IsCurrent)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsCurrent, false), ct);
        policy.IsCurrent = true;
        Audit.Record(db, ctx, g.Principal, "policy.activate", policy.Version);
        await db.SaveChangesAsync(ct);
        return Json(200, new { policy = PolicyView(policy) });
    }

    // ---------- Public policy ----------

    private static async Task<IResult> CurrentPolicy(AppDb db, CancellationToken ct)
    {
        var policy = await RefundPolicyGate.CurrentAsync(db, ct);
        if (policy is null) return Fail(404, "no current refund policy");
        return Json(200, new { policy = PolicyView(policy) });
    }

    private static async Task<IResult> Status(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        if (!EmailAddress.TryNormalize(ctx.Request.Query["email"].ToString(), out var email))
            return Fail(400, "invalid email");

        var current = await RefundPolicyGate.CurrentAsync(db, ct);
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Email == email, ct);
        if (current is null)
            return Json(200, new { email, accepted = true, policyVersion = (string?)null, acceptedAt = (string?)null });
        if (customer is null)
            return Json(200, new { email, accepted = false, policyVersion = current.Version, acceptedAt = (string?)null });

        var row = await db.RefundPolicyAcceptances.AsNoTracking()
            .FirstOrDefaultAsync(a => a.CustomerId == customer.Id && a.PolicyId == current.Id, ct);
        return Json(200, new
        {
            email,
            accepted = row is not null,
            policyVersion = current.Version,
            acceptedAt = row?.AcceptedAt.ToString("o")
        });
    }

    private static async Task<IResult> Accept(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        if (!EmailAddress.TryNormalize(Str(body.Value, "email"), out var email))
            return Fail(400, "invalid email");
        if (!PolicyVersion.TryNormalize(Str(body.Value, "policyVersion"), out var version))
            return Fail(400, "invalid policyVersion");

        var current = await db.RefundPolicies.FirstOrDefaultAsync(p => p.IsCurrent, ct);
        if (current is null) return Fail(404, "no current refund policy");
        if (current.Version != version)
            return Fail(422, "accept the current policy");

        var displayName = Str(body.Value, "displayName");
        var customer = await CustomerStore.FindOrCreateAsync(db, email, displayName, "policy-accept", ct);
        CustomerStore.ApplyPhone(customer, Str(body.Value, "phone"));
        await db.SaveChangesAsync(ct);

        var existing = await db.RefundPolicyAcceptances
            .FirstOrDefaultAsync(a => a.CustomerId == customer.Id && a.PolicyId == current.Id, ct);
        if (existing is not null)
        {
            return Json(200, new
            {
                accepted = true,
                alreadyAccepted = true,
                customerId = customer.Id,
                policyVersion = current.Version,
                acceptedAt = existing.AcceptedAt.ToString("o")
            });
        }

        var acceptance = new RefundPolicyAcceptance
        {
            CustomerId = customer.Id,
            PolicyId = current.Id,
            AcceptedAt = DateTime.UtcNow,
            IpAddress = ctx.Connection.RemoteIpAddress?.ToString() ?? "",
            UserAgent = ctx.Request.Headers.UserAgent.ToString(),
            Source = Str(body.Value, "source") is { Length: > 0 } src ? src : "checkout"
        };
        db.RefundPolicyAcceptances.Add(acceptance);
        Audit.Record(db, ctx, null, "policy.accept", email, $"version={current.Version}");
        await db.SaveChangesAsync(ct);

        return Json(200, new
        {
            accepted = true,
            alreadyAccepted = false,
            customerId = customer.Id,
            policyVersion = current.Version,
            acceptedAt = acceptance.AcceptedAt.ToString("o")
        });
    }

    // ---------- Views / helpers ----------

    private static object Summary(Customer c, bool acceptedCurrent, int licenseCount = 0) => new
    {
        c.Id, c.Email, c.DisplayName, c.Phone, c.Notes, c.Source, c.IsActive, acceptedCurrent, licenseCount,
        createdAt = c.CreatedAt.ToString("o"),
        updatedAt = c.UpdatedAt.ToString("o")
    };

    private static object Detail(Customer c, bool acceptedCurrent, CheckoutSession? checkout) => new
    {
        c.Id, c.Email, c.DisplayName, c.Phone, c.Notes, c.Source, c.ExternalCustomerId, c.IsActive, acceptedCurrent,
        licenseCount = c.Licenses.Count,
        createdAt = c.CreatedAt.ToString("o"),
        updatedAt = c.UpdatedAt.ToString("o"),
        licenses = c.Licenses.OrderByDescending(l => l.Id).Select(l => new
        {
            l.Id, l.Key, l.ProductId, l.PlanType, l.IsActive, l.IsRevoked,
            activatedAt = l.ActivatedAt?.ToString("o"),
            expiresAt = l.ExpiresAt?.ToString("o")
        }),
        acceptances = c.Acceptances.OrderByDescending(a => a.AcceptedAt).Select(a => new
        {
            a.Id, policyVersion = a.Policy.Version, a.Source,
            acceptedAt = a.AcceptedAt.ToString("o")
        }),
        checkout = checkout is null ? null : new
        {
            ip = checkout.IpAddress,
            device = checkout.Device,
            os = checkout.OsVersion,
            browser = checkout.Browser,
            language = checkout.Language,
            timezone = checkout.Timezone,
            screen = checkout.Screen,
            geo = checkout.Geo,
            isp = checkout.Isp,
            productSlug = checkout.ProductSlug,
            planCode = checkout.PlanCode,
            capturedAt = checkout.CreatedAt.ToString("o")
        }
    };

    private static object PolicyView(RefundPolicy p) => new
    {
        p.Id, p.Version, p.Title, p.Body, p.IsCurrent,
        effectiveAt = p.EffectiveAt.ToString("o"),
        createdAt = p.CreatedAt.ToString("o")
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

    private static bool TryBool(JsonElement body, string name, out bool result)
    {
        result = false;
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(name, out var v)) return false;
        if (v.ValueKind == JsonValueKind.True) { result = true; return true; }
        if (v.ValueKind == JsonValueKind.False) { result = false; return true; }
        return false;
    }

    private static IResult Fail(int status, string error) => Json(status, new { error });
    private static IResult Json(int status, object body) => Results.Json(body, JsonOptions, statusCode: status);
}
