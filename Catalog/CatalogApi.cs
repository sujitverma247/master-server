using System.Text.Json;
using MasterServer.Admin;
using MasterServer.Data;
using MasterServer.Security;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Catalog;

public static class CatalogApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Map(WebApplication app, int skewSeconds)
    {
        app.MapGet("/admin/brands", (HttpContext ctx, AppDb db, CancellationToken ct) => ListBrands(ctx, db, ct));
        app.MapPost("/admin/brands", (HttpContext ctx, AppDb db, CancellationToken ct) => CreateBrand(ctx, db, ct));
        app.MapPut("/admin/brands/{id:int}", (int id, HttpContext ctx, AppDb db, CancellationToken ct) => UpdateBrand(id, ctx, db, ct));

        app.MapGet("/admin/products", (HttpContext ctx, AppDb db, CancellationToken ct) => ListProducts(ctx, db, ct));
        app.MapPost("/admin/products", (HttpContext ctx, AppDb db, CancellationToken ct) => CreateProduct(ctx, db, ct));
        app.MapPut("/admin/products/{id:int}", (int id, HttpContext ctx, AppDb db, CancellationToken ct) => UpdateProduct(id, ctx, db, ct));
        app.MapPost("/admin/products/{id:int}/deactivate", (int id, HttpContext ctx, AppDb db, CancellationToken ct) => DeactivateProduct(id, ctx, db, ct));

        app.MapGet("/admin/plans", (HttpContext ctx, AppDb db, CancellationToken ct) => ListPlans(ctx, db, ct));
        app.MapPost("/admin/plans", (HttpContext ctx, AppDb db, CancellationToken ct) => CreatePlan(ctx, db, ct));
        app.MapPut("/admin/plans/{id:int}", (int id, HttpContext ctx, AppDb db, CancellationToken ct) => UpdatePlan(id, ctx, db, ct));

        app.MapGet("/v1/app/update-check", (HttpContext ctx, AppDb db, CancellationToken ct) => UpdateCheck(ctx, db, skewSeconds, ct));
    }

    // ---------- Brands ----------

    private static async Task<IResult> ListBrands(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var p = await Require(ctx, db, Permissions.ProductRead, ct);
        if (p.Result is not null) return p.Result;

        var brands = await db.Brands.OrderBy(b => b.Id).ToListAsync(ct);
        return Json(200, new { brands = brands.Select(BrandView) });
    }

    private static async Task<IResult> CreateBrand(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var p = await Require(ctx, db, Permissions.ProductWrite, ct);
        if (p.Result is not null) return p.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");

        var slug = Str(body.Value, "slug");
        var name = Str(body.Value, "name");
        if (!IsSlug(slug)) return Fail(400, "slug must be 2-64 chars of a-z, 0-9, hyphen");
        if (string.IsNullOrWhiteSpace(name)) return Fail(400, "missing name");
        if (await db.Brands.AnyAsync(b => b.Slug == slug, ct)) return Fail(409, "slug already exists");

        var brand = new Brand
        {
            Slug = slug,
            Name = name,
            SupportEmail = Str(body.Value, "supportEmail"),
            WebsiteUrl = Str(body.Value, "websiteUrl"),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Brands.Add(brand);
        Audit.Record(db, ctx, p.Principal, "brand.create", slug);
        await db.SaveChangesAsync(ct);
        return Json(201, new { brand = BrandView(brand) });
    }

    private static async Task<IResult> UpdateBrand(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var p = await Require(ctx, db, Permissions.ProductWrite, ct);
        if (p.Result is not null) return p.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");

        var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (brand is null) return Fail(404, "brand not found");

        if (Str(body.Value, "name") is { Length: > 0 } name) brand.Name = name;
        if (body.Value.TryGetProperty("supportEmail", out _)) brand.SupportEmail = Str(body.Value, "supportEmail");
        if (body.Value.TryGetProperty("websiteUrl", out _)) brand.WebsiteUrl = Str(body.Value, "websiteUrl");
        if (TryBool(body.Value, "isActive", out var active)) brand.IsActive = active;

        Audit.Record(db, ctx, p.Principal, "brand.update", brand.Slug);
        await db.SaveChangesAsync(ct);
        return Json(200, new { brand = BrandView(brand) });
    }

    // ---------- Products ----------

    private static async Task<IResult> ListProducts(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var p = await Require(ctx, db, Permissions.ProductRead, ct);
        if (p.Result is not null) return p.Result;

        var products = await db.Products.OrderBy(x => x.Id).ToListAsync(ct);
        return Json(200, new { products = products.Select(ProductView) });
    }

    private static async Task<IResult> CreateProduct(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var p = await Require(ctx, db, Permissions.ProductWrite, ct);
        if (p.Result is not null) return p.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");

        var slug = Str(body.Value, "slug");
        var appKey = Str(body.Value, "appKey");
        var userAgentPrefix = Str(body.Value, "userAgentPrefix");
        var hmacSecret = Str(body.Value, "hmacSecret");
        var keyPrefix = Str(body.Value, "keyPrefix");
        var displayName = Str(body.Value, "displayName");

        if (!IsSlug(slug)) return Fail(400, "slug must be 2-64 chars of a-z, 0-9, hyphen");
        if (string.IsNullOrWhiteSpace(appKey)) return Fail(400, "missing appKey");
        if (string.IsNullOrWhiteSpace(userAgentPrefix)) return Fail(400, "missing userAgentPrefix");
        if (string.IsNullOrWhiteSpace(hmacSecret)) return Fail(400, "missing hmacSecret");
        if (await db.Products.AnyAsync(x => x.Slug == slug, ct)) return Fail(409, "slug already exists");
        if (await db.Products.AnyAsync(x => x.AppKey == appKey, ct)) return Fail(409, "appKey already exists");

        int? brandId = null;
        if (TryInt(body.Value, "brandId", out var bid))
        {
            if (!await db.Brands.AnyAsync(b => b.Id == bid, ct)) return Fail(422, "unknown brandId");
            brandId = bid;
        }

        var product = new Product
        {
            BrandId = brandId,
            Slug = slug,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? slug : displayName,
            AppKey = appKey,
            KeyPrefix = keyPrefix,
            UserAgentPrefix = userAgentPrefix,
            HmacSecret = hmacSecret,
            LatestVersion = Str(body.Value, "latestVersion"),
            DownloadUrl = NullIfEmpty(Str(body.Value, "downloadUrl")),
            ReleaseNotes = NullIfEmpty(Str(body.Value, "releaseNotes")),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Products.Add(product);
        Audit.Record(db, ctx, p.Principal, "product.create", slug);
        await db.SaveChangesAsync(ct);
        return Json(201, new { product = ProductView(product) });
    }

    private static async Task<IResult> UpdateProduct(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var p = await Require(ctx, db, Permissions.ProductWrite, ct);
        if (p.Result is not null) return p.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");

        var product = await db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (product is null) return Fail(404, "product not found");

        var changes = new List<string>();
        if (Str(body.Value, "displayName") is { Length: > 0 } dn) { product.DisplayName = dn; changes.Add("displayName"); }
        if (body.Value.TryGetProperty("keyPrefix", out _)) { product.KeyPrefix = Str(body.Value, "keyPrefix"); changes.Add("keyPrefix"); }
        if (Str(body.Value, "userAgentPrefix") is { Length: > 0 } ua) { product.UserAgentPrefix = ua; changes.Add("userAgentPrefix"); }
        if (Str(body.Value, "hmacSecret") is { Length: > 0 } hs) { product.HmacSecret = hs; changes.Add("hmacSecret"); }
        if (body.Value.TryGetProperty("latestVersion", out _)) { product.LatestVersion = Str(body.Value, "latestVersion"); changes.Add("latestVersion"); }
        if (body.Value.TryGetProperty("downloadUrl", out _)) { product.DownloadUrl = NullIfEmpty(Str(body.Value, "downloadUrl")); changes.Add("downloadUrl"); }
        if (body.Value.TryGetProperty("releaseNotes", out _)) { product.ReleaseNotes = NullIfEmpty(Str(body.Value, "releaseNotes")); changes.Add("releaseNotes"); }
        if (TryBool(body.Value, "isActive", out var active)) { product.IsActive = active; changes.Add($"isActive={active}"); }
        if (TryInt(body.Value, "brandId", out var bid))
        {
            if (bid == 0) { product.BrandId = null; }
            else
            {
                if (!await db.Brands.AnyAsync(b => b.Id == bid, ct)) return Fail(422, "unknown brandId");
                product.BrandId = bid;
            }
            changes.Add("brandId");
        }

        Audit.Record(db, ctx, p.Principal, "product.update", product.Slug, string.Join(",", changes));
        await db.SaveChangesAsync(ct);
        return Json(200, new { product = ProductView(product) });
    }

    private static async Task<IResult> DeactivateProduct(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var p = await Require(ctx, db, Permissions.ProductWrite, ct);
        if (p.Result is not null) return p.Result;

        var product = await db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (product is null) return Fail(404, "product not found");

        product.IsActive = false;
        Audit.Record(db, ctx, p.Principal, "product.deactivate", product.Slug);
        await db.SaveChangesAsync(ct);
        return Json(200, new { product = ProductView(product) });
    }

    // ---------- Plans ----------

    private static async Task<IResult> ListPlans(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var p = await Require(ctx, db, Permissions.ProductRead, ct);
        if (p.Result is not null) return p.Result;

        var plans = await db.Plans.OrderBy(x => x.Id).ToListAsync(ct);
        return Json(200, new { plans = plans.Select(PlanView) });
    }

    private static async Task<IResult> CreatePlan(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var p = await Require(ctx, db, Permissions.PlanWrite, ct);
        if (p.Result is not null) return p.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");

        var code = Str(body.Value, "code").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(code)) return Fail(400, "missing code");
        if (!TryInt(body.Value, "durationDays", out var durationDays) || durationDays <= 0)
            return Fail(400, "durationDays must be a positive integer");
        if (await db.Plans.AnyAsync(x => x.Code == code, ct)) return Fail(409, "code already exists");

        var maxMachines = TryInt(body.Value, "defaultMaxMachines", out var mm) ? Math.Clamp(mm, 1, 100) : 3;
        var plan = new Plan
        {
            Code = code,
            DisplayName = Str(body.Value, "displayName") is { Length: > 0 } d ? d : code,
            DurationDays = durationDays,
            DefaultMaxMachines = maxMachines,
            Price = TryDecimal(body.Value, "price", out var price) ? price : 0m,
            Currency = Str(body.Value, "currency").ToUpperInvariant(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Plans.Add(plan);
        Audit.Record(db, ctx, p.Principal, "plan.create", code);
        await db.SaveChangesAsync(ct);
        return Json(201, new { plan = PlanView(plan) });
    }

    private static async Task<IResult> UpdatePlan(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var p = await Require(ctx, db, Permissions.PlanWrite, ct);
        if (p.Result is not null) return p.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");

        var plan = await db.Plans.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (plan is null) return Fail(404, "plan not found");

        if (Str(body.Value, "displayName") is { Length: > 0 } dn) plan.DisplayName = dn;
        if (TryInt(body.Value, "durationDays", out var dd) && dd > 0) plan.DurationDays = dd;
        if (TryInt(body.Value, "defaultMaxMachines", out var mm)) plan.DefaultMaxMachines = Math.Clamp(mm, 1, 100);
        if (TryDecimal(body.Value, "price", out var price)) plan.Price = price;
        if (body.Value.TryGetProperty("currency", out _)) plan.Currency = Str(body.Value, "currency").ToUpperInvariant();
        if (TryBool(body.Value, "isActive", out var active)) plan.IsActive = active;

        Audit.Record(db, ctx, p.Principal, "plan.update", plan.Code);
        await db.SaveChangesAsync(ct);
        return Json(200, new { plan = PlanView(plan) });
    }

    // ---------- Public update-check (HMAC) ----------

    private static async Task<IResult> UpdateCheck(HttpContext ctx, AppDb db, int skewSeconds, CancellationToken ct)
    {
        var auth = await HmacRequest.AuthenticateAsync(ctx, db, skewSeconds, ct);
        if (auth is not HmacResult.Ok ok)
            return Fail(401, "unauthorized");

        var current = ctx.Request.Query["version"].ToString().Trim();
        var latest = ok.Product.LatestVersion ?? "";
        var updateAvailable = latest.Length > 0 && !string.Equals(current, latest, StringComparison.OrdinalIgnoreCase);

        return Json(200, new
        {
            latestVersion = latest,
            downloadUrl = ok.Product.DownloadUrl ?? "",
            releaseNotes = ok.Product.ReleaseNotes ?? "",
            updateAvailable
        });
    }

    // ---------- Views ----------

    private static object BrandView(Brand b) => new
    {
        b.Id, b.Slug, b.Name, b.SupportEmail, b.WebsiteUrl, b.IsActive, createdAt = b.CreatedAt.ToString("o")
    };

    private static object ProductView(Product p) => new
    {
        p.Id,
        p.BrandId,
        p.Slug,
        p.DisplayName,
        p.AppKey,
        p.KeyPrefix,
        p.UserAgentPrefix,
        hasHmacSecret = !string.IsNullOrEmpty(p.HmacSecret),
        p.LatestVersion,
        downloadUrl = p.DownloadUrl ?? "",
        releaseNotes = p.ReleaseNotes ?? "",
        p.IsActive,
        createdAt = p.CreatedAt.ToString("o")
    };

    private static object PlanView(Plan p) => new
    {
        p.Id, p.Code, p.DisplayName, p.DurationDays, p.DefaultMaxMachines, p.Price, p.Currency, p.IsActive,
        createdAt = p.CreatedAt.ToString("o")
    };

    // ---------- Helpers ----------

    private readonly record struct Gate(IResult? Result, AdminPrincipal? Principal);

    private static async Task<Gate> Require(HttpContext ctx, AppDb db, string permission, CancellationToken ct)
    {
        var principal = await AdminAuth.ResolveAsync(ctx, db, ct);
        if (principal is null)
            return new Gate(Fail(401, "unauthorized"), null);
        if (!principal.Has(permission))
            return new Gate(Fail(403, "forbidden"), null);
        return new Gate(null, principal);
    }

    private static bool IsSlug(string s) =>
        s.Length is >= 2 and <= 64 && s.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static async Task<JsonElement?> ReadBodyAsync(HttpContext ctx, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ct);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Str(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object
        && body.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? "").Trim()
            : "";

    private static bool TryInt(JsonElement body, string name, out int result)
    {
        result = 0;
        return body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out result);
    }

    private static bool TryDecimal(JsonElement body, string name, out decimal result)
    {
        result = 0m;
        return body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out result);
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

    private static IResult Fail(int status, string error) => Json(status, new { error });
    private static IResult Json(int status, object body) => Results.Json(body, JsonOptions, statusCode: status);
}
