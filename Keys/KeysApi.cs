using System.Text;
using System.Text.Json;
using MasterServer.Admin;
using MasterServer.Catalog;
using MasterServer.Customers;
using MasterServer.Data;
using MasterServer.Licensing;
using MasterServer.Mail;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Keys;

public static class KeysApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxBatch = 5000;

    public static void Map(WebApplication app, IMailSender mail)
    {
        app.MapPost("/admin/keys/generate", (HttpContext c, AppDb db, CancellationToken ct) => Generate(c, db, ct));
        app.MapPost("/admin/keys/issue", (HttpContext c, AppDb db, CancellationToken ct) => Issue(c, db, mail, ct));
        app.MapPost("/panel/keys/issue", (HttpContext c, AppDb db, CancellationToken ct) => Issue(c, db, mail, ct));
        app.MapGet("/admin/keys", (HttpContext c, AppDb db, CancellationToken ct) => List(c, db, ct));
        app.MapGet("/admin/keys/export", (HttpContext c, AppDb db, CancellationToken ct) => Export(c, db, ct));
        app.MapGet("/admin/keys/{id:int}", (int id, HttpContext c, AppDb db, CancellationToken ct) => Get(id, c, db, ct));
        app.MapGet("/admin/keys/{id:int}/evidence", (int id, HttpContext c, AppDb db, CancellationToken ct) => Evidence(id, c, db, ct));
        app.MapPost("/admin/keys/revoke", (HttpContext c, AppDb db, CancellationToken ct) => Revoke(c, db, ct));
        app.MapPost("/admin/keys/revoke-batch", (HttpContext c, AppDb db, CancellationToken ct) => RevokeBatch(c, db, ct));
        app.MapPost("/admin/keys/update", (HttpContext c, AppDb db, CancellationToken ct) => Update(c, db, ct));
        app.MapPost("/admin/keys/delete", (HttpContext c, AppDb db, CancellationToken ct) => Delete(c, db, ct));
        app.MapGet("/agent/lookup", (HttpContext c, AppDb db, CancellationToken ct) => Lookup(c, db, ct));
    }

    // ---------- Generate a batch ----------

    private static async Task<IResult> Generate(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.KeyIssue, ct);
        if (g.Result is not null) return g.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");

        if (!TryInt(body.Value, "productId", out var productId))
            return Fail(400, "missing productId");
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null) return Fail(422, "unknown productId");

        if (!TryInt(body.Value, "quantity", out var quantity) || quantity < 1)
            return Fail(400, "quantity must be a positive integer");
        if (quantity > MaxBatch) return Fail(400, $"quantity exceeds {MaxBatch}");

        var planType = Str(body.Value, "planType");
        if (string.IsNullOrWhiteSpace(planType)) planType = "1year";
        var maxMachines = TryInt(body.Value, "maxMachines", out var mm) ? Math.Clamp(mm, 1, 100) : await DefaultMaxMachines(db, planType, ct);

        var batch = new KeyBatch
        {
            Name = Str(body.Value, "name") is { Length: > 0 } n ? n : $"{product.Slug}-{DateTime.UtcNow:yyyyMMddHHmmss}",
            ProductId = productId,
            PlanType = planType,
            Quantity = quantity,
            MaxMachines = maxMachines,
            Notes = Str(body.Value, "notes"),
            GeneratedBy = g.Principal!.User.Username,
            GeneratedAt = DateTime.UtcNow
        };
        db.KeyBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        var keys = await GenerateUniqueKeys(db, product.KeyPrefix, quantity, ct);
        var now = DateTime.UtcNow;
        var expiresAt = await LicenseTerm.FromPurchaseAsync(db, planType, now, ct);
        foreach (var key in keys)
        {
            db.Licenses.Add(new License
            {
                Key = key,
                ProductId = productId,
                BatchId = batch.Id,
                PlanType = planType,
                MaxMachines = maxMachines,
                CreatedAt = now,
                ExpiresAt = expiresAt,
                IsActive = true,
                IsRevoked = false
            });
        }
        Audit.Record(db, ctx, g.Principal, "key.generate", $"batch#{batch.Id}", $"product={product.Slug},plan={planType},qty={quantity}");
        await db.SaveChangesAsync(ct);

        return Json(201, new { batchId = batch.Id, batchName = batch.Name, quantity, planType, maxMachines, keys });
    }

    // ---------- Issue a single key ----------

    private static async Task<IResult> Issue(HttpContext ctx, AppDb db, IMailSender mail, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.KeyIssue, ct);
        if (g.Result is not null) return g.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");

        if (!TryInt(body.Value, "productId", out var productId))
            return Fail(400, "missing productId");
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.IsActive, ct);
        if (product is null) return Fail(422, "unknown productId");

        var planType = Str(body.Value, "planType").ToLowerInvariant();
        if (planType.Length == 0) planType = "1year";
        var maxMachines = TryInt(body.Value, "maxMachines", out var mm) ? Math.Clamp(mm, 1, 100) : await DefaultMaxMachines(db, planType, ct);

        var explicitKey = Str(body.Value, "key").ToUpperInvariant();
        string key;
        if (explicitKey.Length > 0)
        {
            if (await db.Licenses.AnyAsync(l => l.Key == explicitKey, ct))
                return Fail(409, "key already exists");
            key = explicitKey;
        }
        else
        {
            key = (await GenerateUniqueKeys(db, product.KeyPrefix, 1, ct))[0];
        }

        var rawEmail = FirstNonEmpty(Str(body.Value, "customerEmail"), Str(body.Value, "email"));
        if (!EmailAddress.TryNormalize(rawEmail, out var email))
            return Fail(400, "invalid customerEmail");
        var displayName = Str(body.Value, "name");
        if (displayName.Length > 200) return Fail(400, "name too long");
        var customer = await CustomerStore.FindOrCreateAsync(db, email, displayName, "key-issue", ct);

        var notes = Str(body.Value, "notes");
        if (notes.Length > 1024) return Fail(400, "notes too long");

        var purchasedAt = DateTime.UtcNow;
        var license = new License
        {
            Key = key,
            ProductId = productId,
            PlanType = planType,
            MaxMachines = maxMachines,
            CreatedAt = purchasedAt,
            ExpiresAt = await LicenseTerm.FromPurchaseAsync(db, planType, purchasedAt, ct),
            IsActive = true,
            IsRevoked = false,
            Customer = customer,
            CustomerEmail = email,
            Notes = notes,
            Source = ""
        };
        db.Licenses.Add(license);
        await db.SaveChangesAsync(ct);

        Audit.Record(db, ctx, g.Principal, "key.issue", $"license#{license.Id}", $"product={product.Slug},plan={planType},keyEnds={Mask(key)}");
        await MailOutbox.EnqueueLicenseIssuedAsync(db, license, null, ct);
        await db.SaveChangesAsync(ct);
        await MailDispatch.DrainAsync(db, mail, 20, ct);

        return Json(201, new
        {
            key = license.Key,
            email,
            name = customer.DisplayName,
            productName = product.DisplayName,
            productSlug = product.Slug,
            planType,
            maxMachines,
            purchasedAt = license.CreatedAt.ToString("o"),
            activatedAt = (string?)null,
            expiresAt = license.ExpiresAt?.ToString("o"),
            license = Detail(license, product, null)
        });
    }

    // ---------- List ----------

    private static async Task<IResult> List(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.KeyRead, ct);
        if (g.Result is not null) return g.Result;

        var q = db.Licenses.AsNoTracking().AsQueryable();
        if (int.TryParse(ctx.Request.Query["productId"], out var pid)) q = q.Where(l => l.ProductId == pid);
        if (int.TryParse(ctx.Request.Query["batchId"], out var bid)) q = q.Where(l => l.BatchId == bid);

        var planType = ctx.Request.Query["planType"].ToString().Trim().ToLowerInvariant();
        if (planType.Length > 0) q = q.Where(l => l.PlanType.ToLower() == planType);

        var now = DateTime.UtcNow;
        var status = ctx.Request.Query["status"].ToString().ToLowerInvariant();
        q = status switch
        {
            "revoked" => q.Where(l => l.IsRevoked),
            "inactive" => q.Where(l => !l.IsActive && !l.IsRevoked),
            "active" => q.Where(l => l.IsActive && !l.IsRevoked && l.ActivatedAt != null && (l.ExpiresAt == null || l.ExpiresAt >= now)),
            "unused" => q.Where(l => !l.IsRevoked && l.ActivatedAt == null && (l.ExpiresAt == null || l.ExpiresAt >= now)),
            "activated" => q.Where(l => l.ActivatedAt != null),
            "expired" => q.Where(l => !l.IsRevoked && l.ExpiresAt != null && l.ExpiresAt < now),
            _ => q
        };

        var search = FirstNonEmpty(ctx.Request.Query["q"].ToString(), ctx.Request.Query["search"].ToString()).Trim();
        if (search.Length > 0)
        {
            var s = search.ToUpperInvariant();
            q = q.Where(l => l.Key.ToUpper().Contains(s) || l.CustomerEmail.ToUpper().Contains(s));
        }

        if (DateOnly.TryParse(ctx.Request.Query["from"], out var fromDay))
        {
            var fromUtc = fromDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(l => l.CreatedAt >= fromUtc);
        }
        if (DateOnly.TryParse(ctx.Request.Query["to"], out var toDay))
        {
            var toExclusive = toDay.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(l => l.CreatedAt < toExclusive);
        }

        var projected = q.Select(l => new
        {
            License = l,
            ProductName = db.Products.Where(p => p.Id == l.ProductId).Select(p => p.DisplayName).FirstOrDefault() ?? "",
            ProductSlug = db.Products.Where(p => p.Id == l.ProductId).Select(p => p.Slug).FirstOrDefault() ?? "",
            PurchasedAt = db.Orders.Where(o => o.LicenseId == l.Id).Select(o => (DateTime?)(o.PaidAt ?? o.CreatedAt)).FirstOrDefault() ?? l.CreatedAt
        });

        var sort = ctx.Request.Query["sort"].ToString().Trim().ToLowerInvariant();
        var asc = ctx.Request.Query["dir"].ToString().Trim().Equals("asc", StringComparison.OrdinalIgnoreCase);
        projected = sort switch
        {
            "key" => asc ? projected.OrderBy(x => x.License.Key) : projected.OrderByDescending(x => x.License.Key),
            "product" => asc ? projected.OrderBy(x => x.ProductName) : projected.OrderByDescending(x => x.ProductName),
            "plan" => asc ? projected.OrderBy(x => x.License.PlanType) : projected.OrderByDescending(x => x.License.PlanType),
            "customer" => asc ? projected.OrderBy(x => x.License.CustomerEmail) : projected.OrderByDescending(x => x.License.CustomerEmail),
            "source" => asc ? projected.OrderBy(x => x.License.Source) : projected.OrderByDescending(x => x.License.Source),
            "status" => asc ? projected.OrderBy(x => x.License.IsRevoked).ThenBy(x => x.License.ActivatedAt) : projected.OrderByDescending(x => x.License.IsRevoked).ThenByDescending(x => x.License.ActivatedAt),
            "purchased" => asc ? projected.OrderBy(x => x.PurchasedAt) : projected.OrderByDescending(x => x.PurchasedAt),
            "activated" => asc ? projected.OrderBy(x => x.License.ActivatedAt) : projected.OrderByDescending(x => x.License.ActivatedAt),
            "expires" => asc ? projected.OrderBy(x => x.License.ExpiresAt) : projected.OrderByDescending(x => x.License.ExpiresAt),
            _ => asc ? projected.OrderBy(x => x.License.CreatedAt) : projected.OrderByDescending(x => x.License.CreatedAt)
        };

        var total = await projected.CountAsync(ct);
        var limit = CatalogPaging.NormalizeLimit(ctx.Request.Query["limit"].ToString());
        var page = CatalogPaging.ClampPage(CatalogPaging.NormalizePage(ctx.Request.Query["page"].ToString()), total, limit);
        var rows = await projected.Skip((page - 1) * limit).Take(limit).ToListAsync(ct);

        var allCount = await db.Licenses.CountAsync(ct);
        var unusedCount = await db.Licenses.CountAsync(l => !l.IsRevoked && l.ActivatedAt == null && (l.ExpiresAt == null || l.ExpiresAt >= now), ct);
        var activeCount = await db.Licenses.CountAsync(l => l.IsActive && !l.IsRevoked && l.ActivatedAt != null && (l.ExpiresAt == null || l.ExpiresAt >= now), ct);
        var revokedCount = await db.Licenses.CountAsync(l => l.IsRevoked, ct);
        var planTypes = await db.Licenses.Select(l => l.PlanType).Distinct().OrderBy(x => x).ToListAsync(ct);

        return Json(200, new
        {
            keys = rows.Select(x => Summary(x.License, x.ProductName, x.ProductSlug, now, x.PurchasedAt)),
            total,
            page,
            limit,
            counts = new { total = allCount, unused = unusedCount, active = activeCount, revoked = revokedCount },
            planTypes
        });
    }

    private static async Task<IResult> Get(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.KeyRead, ct);
        if (g.Result is not null) return g.Result;

        var license = await db.Licenses.AsNoTracking().Include(l => l.Activations).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (license is null) return Fail(404, "key not found");
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == license.ProductId, ct);
        return Json(200, new { license = Detail(license, product, license.Activations) });
    }

    private static async Task<IResult> Evidence(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.KeyRead, ct);
        if (g.Result is not null) return g.Result;

        var license = await db.Licenses
            .Include(l => l.Product).ThenInclude(p => p.Brand)
            .Include(l => l.Customer)
            .Include(l => l.Activations)
            .FirstOrDefaultAsync(l => l.Id == id, ct);
        if (license is null) return Fail(404, "key not found");

        EvidenceDocument data;
        try { data = await EvidenceComposer.BuildAsync(db, license, g.Principal!.User.Username, ct); }
        catch
        {
            return Fail(500, "failed to assemble the evidence document");
        }

        byte[] pdf;
        try { pdf = EvidencePdf.Render(data); }
        catch
        {
            return Fail(500, "failed to generate the evidence document");
        }

        Audit.Record(db, ctx, g.Principal, "evidence.generate", $"license#{license.Id}", data.ReportId);
        await db.SaveChangesAsync(ct);
        return Results.File(pdf, "application/pdf", EvidencePdf.FileName(license.Key, data.GeneratedAtUtc));
    }

    // ---------- Revoke / update / delete ----------

    private static async Task<IResult> Revoke(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.KeyRevoke, ct);
        if (g.Result is not null) return g.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        var license = await FindTarget(db, body.Value, ct);
        if (license is null) return Fail(404, "key not found");

        license.IsRevoked = true;
        Audit.Record(db, ctx, g.Principal, "key.revoke", $"license#{license.Id}", $"keyEnds={Mask(license.Key)}");
        await db.SaveChangesAsync(ct);
        return Json(200, new { id = license.Id, isRevoked = true });
    }

    private static async Task<IResult> RevokeBatch(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.KeyRevoke, ct);
        if (g.Result is not null) return g.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        if (!TryInt(body.Value, "batchId", out var batchId)) return Fail(400, "missing batchId");
        if (!await db.KeyBatches.AnyAsync(b => b.Id == batchId, ct)) return Fail(404, "batch not found");

        var affected = await db.Licenses.Where(l => l.BatchId == batchId && !l.IsRevoked)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.IsRevoked, true), ct);
        Audit.Record(db, ctx, g.Principal, "key.revoke-batch", $"batch#{batchId}", $"revoked={affected}");
        await db.SaveChangesAsync(ct);
        return Json(200, new { batchId, revoked = affected });
    }

    private static async Task<IResult> Update(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.KeyRevoke, ct);
        if (g.Result is not null) return g.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        var license = await FindTarget(db, body.Value, ct);
        if (license is null) return Fail(404, "key not found");

        var changes = new List<string>();
        if (Str(body.Value, "planType") is { Length: > 0 } pt && !string.Equals(license.PlanType, pt, StringComparison.OrdinalIgnoreCase))
        {
            license.PlanType = pt;
            changes.Add("planType");
            if (!license.IsSubscription)
            {
                var purchased = await db.Orders.Where(o => o.LicenseId == license.Id)
                    .Select(o => (DateTime?)(o.PaidAt ?? o.CreatedAt))
                    .FirstOrDefaultAsync(ct) ?? license.CreatedAt;
                license.ExpiresAt = await LicenseTerm.FromPurchaseAsync(db, pt, purchased, ct);
                changes.Add("expiresAt");
            }
        }
        if (TryInt(body.Value, "maxMachines", out var mm)) { license.MaxMachines = Math.Clamp(mm, 1, 100); changes.Add("maxMachines"); }
        if (body.Value.TryGetProperty("notes", out _)) { license.Notes = Str(body.Value, "notes"); changes.Add("notes"); }
        if (body.Value.TryGetProperty("customerEmail", out _))
        {
            var rawEmail = Str(body.Value, "customerEmail");
            if (rawEmail.Length == 0)
            {
                license.CustomerEmail = "";
                license.CustomerId = null;
            }
            else
            {
                if (!EmailAddress.TryNormalize(rawEmail, out var email))
                    return Fail(400, "invalid customerEmail");
                var customer = await CustomerStore.FindOrCreateAsync(db, email, "", "key-update", ct);
                license.Customer = customer;
                license.CustomerEmail = email;
            }
            changes.Add("customerEmail");
        }
        if (TryBool(body.Value, "isActive", out var active)) { license.IsActive = active; changes.Add($"isActive={active}"); }
        if (TryBool(body.Value, "isRevoked", out var revoked)) { license.IsRevoked = revoked; changes.Add($"isRevoked={revoked}"); }

        Audit.Record(db, ctx, g.Principal, "key.update", $"license#{license.Id}", string.Join(",", changes));
        await db.SaveChangesAsync(ct);
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == license.ProductId, ct);
        return Json(200, new { license = Detail(license, product, null) });
    }

    private static async Task<IResult> Delete(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.KeyRevoke, ct);
        if (g.Result is not null) return g.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");
        var license = await FindTarget(db, body.Value, ct);
        if (license is null) return Fail(404, "key not found");

        var id = license.Id;
        db.Licenses.Remove(license);
        Audit.Record(db, ctx, g.Principal, "key.delete", $"license#{id}", $"keyEnds={Mask(license.Key)}");
        await db.SaveChangesAsync(ct);
        return Json(200, new { id, deleted = true });
    }

    // ---------- Export ----------

    private static async Task<IResult> Export(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.KeyRead, ct);
        if (g.Result is not null) return g.Result;

        var q = db.Licenses.AsNoTracking().AsQueryable();
        if (int.TryParse(ctx.Request.Query["batchId"], out var bid)) q = q.Where(l => l.BatchId == bid);
        if (int.TryParse(ctx.Request.Query["productId"], out var pid)) q = q.Where(l => l.ProductId == pid);

        var rows = await q.OrderBy(l => l.Id).Take(MaxBatch).ToListAsync(ct);
        var sb = new StringBuilder();
        sb.AppendLine("key,productId,planType,maxMachines,isActive,isRevoked,activatedAt,expiresAt,customerEmail");
        foreach (var l in rows)
            sb.AppendLine($"{l.Key},{l.ProductId},{l.PlanType},{l.MaxMachines},{l.IsActive},{l.IsRevoked},{l.ActivatedAt:o},{l.ExpiresAt:o},{Csv(l.CustomerEmail)}");

        Audit.Record(db, ctx, g.Principal, "key.export", bid > 0 ? $"batch#{bid}" : "all", $"rows={rows.Count}");
        await db.SaveChangesAsync(ct);
        return Results.Text(sb.ToString(), "text/csv");
    }

    // ---------- Agent lookup ----------

    private static async Task<IResult> Lookup(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var g = await Require(ctx, db, Permissions.KeyRead, ct);
        if (g.Result is not null) return g.Result;

        var key = ctx.Request.Query["key"].ToString().Trim();
        var email = ctx.Request.Query["email"].ToString().Trim();
        var subscription = ctx.Request.Query["subscription"].ToString().Trim();

        IQueryable<License> q = db.Licenses.AsNoTracking();
        string auditTarget;
        if (key.Length > 0) { var k = key.ToUpperInvariant(); q = q.Where(l => l.Key == k); auditTarget = $"key={Mask(k)}"; }
        else if (email.Length > 0) { var e = email.ToUpperInvariant(); q = q.Where(l => l.CustomerEmail.ToUpper() == e); auditTarget = $"email={email}"; }
        else if (subscription.Length > 0) { q = q.Where(l => l.ExternalSubscriptionId == subscription); auditTarget = $"subscription={subscription}"; }
        else return Fail(400, "provide key, email, or subscription");

        var rows = await q.OrderByDescending(l => l.Id).Take(50).ToListAsync(ct);
        Audit.Record(db, ctx, g.Principal, "key.lookup", auditTarget, $"results={rows.Count}");
        await db.SaveChangesAsync(ct);
        return Json(200, new { results = rows.Select(Summary) });
    }

    // ---------- Helpers ----------

    private static async Task<List<string>> GenerateUniqueKeys(AppDb db, string prefix, int count, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (set.Count < count)
            set.Add(KeyGenerator.Generate(prefix));

        // Drop any that already exist in the database, then top up until we have `count` fresh keys.
        var existing = await db.Licenses.Where(l => set.Contains(l.Key)).Select(l => l.Key).ToListAsync(ct);
        foreach (var e in existing) set.Remove(e);
        while (set.Count < count)
        {
            var candidate = KeyGenerator.Generate(prefix);
            if (!set.Contains(candidate) && !await db.Licenses.AnyAsync(l => l.Key == candidate, ct))
                set.Add(candidate);
        }
        return set.Take(count).ToList();
    }

    private static async Task<int> DefaultMaxMachines(AppDb db, string planType, CancellationToken ct)
    {
        var code = planType.Trim().ToLower();
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.IsActive && p.Code.ToLower() == code, ct);
        return plan?.DefaultMaxMachines ?? 3;
    }

    private static async Task<License?> FindTarget(AppDb db, JsonElement body, CancellationToken ct)
    {
        if (TryInt(body, "id", out var id))
            return await db.Licenses.FirstOrDefaultAsync(l => l.Id == id, ct);
        var key = Str(body, "key").ToUpperInvariant();
        if (key.Length > 0)
            return await db.Licenses.FirstOrDefaultAsync(l => l.Key == key, ct);
        return null;
    }

    private static object Summary(License l) => Summary(l, "", "", DateTime.UtcNow, l.CreatedAt);

    private static object Summary(License l, string productName, string productSlug, DateTime now, DateTime purchasedAt) => new
    {
        l.Id, l.Key, l.ProductId, productName, productSlug, l.BatchId, l.CustomerId, l.PlanType, l.MaxMachines,
        l.IsActive, l.IsRevoked, l.Source, l.CustomerEmail, l.Notes,
        status = LicenseStatus(l, now),
        purchasedAt = purchasedAt.ToString("o"),
        activatedAt = l.ActivatedAt?.ToString("o"),
        expiresAt = l.ExpiresAt?.ToString("o"),
        createdAt = l.CreatedAt.ToString("o")
    };

    private static string LicenseStatus(License l, DateTime now)
    {
        if (l.IsRevoked) return "revoked";
        if (!l.IsActive) return "inactive";
        if (LicenseTerm.IsExpired(l.ExpiresAt, now)) return "expired";
        if (l.ActivatedAt is null) return "unused";
        return "active";
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        return "";
    }

    private static object Detail(License l, Product? product, IEnumerable<Activation>? activations) => new
    {
        l.Id, l.Key, l.ProductId, productSlug = product?.Slug, l.BatchId, l.PlanType, l.MaxMachines,
        l.IsActive, l.IsRevoked, l.Notes, l.Source, l.IsSubscription,
        l.ExternalSubscriptionId, l.ExternalPaymentId, l.ExternalCustomerId, l.CustomerId, l.CustomerEmail,
        activatedAt = l.ActivatedAt?.ToString("o"),
        expiresAt = l.ExpiresAt?.ToString("o"),
        createdAt = l.CreatedAt.ToString("o"),
        activations = activations?.Select(a => new
        {
            a.Id, a.MachineId, a.IpAddress, a.AppVersion, a.OsVersion,
            activatedAt = a.ActivatedAt.ToString("o"),
            lastSeenAt = (a.LastSeenAt ?? a.ActivatedAt).ToString("o"),
            deactivatedAt = a.DeactivatedAt?.ToString("o")
        })
    };

    private static string Mask(string key) => key.Length <= 4 ? "****" : "****" + key[^4..];
    private static string Csv(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

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
