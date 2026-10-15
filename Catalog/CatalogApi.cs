using System.Security.Cryptography;
using System.Text.Json;
using MasterServer.Admin;
using MasterServer.Customers;
using MasterServer.Data;
using MasterServer.Licensing;
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
        app.MapDelete("/admin/brands/{id:int}", (int id, HttpContext ctx, AppDb db, CancellationToken ct) => DeleteBrand(id, ctx, db, ct));

        app.MapGet("/admin/products", (HttpContext ctx, AppDb db, CancellationToken ct) => ListProducts(ctx, db, ct));
        app.MapPost("/admin/products", (HttpContext ctx, AppDb db, CancellationToken ct) => CreateProduct(ctx, db, ct));
        app.MapPut("/admin/products/{id:int}", (int id, HttpContext ctx, AppDb db, CancellationToken ct) => UpdateProduct(id, ctx, db, ct));
        app.MapDelete("/admin/products/{id:int}", (int id, HttpContext ctx, AppDb db, CancellationToken ct) => DeleteProduct(id, ctx, db, ct));
        app.MapPost("/admin/products/{id:int}/deactivate", (int id, HttpContext ctx, AppDb db, CancellationToken ct) => DeactivateProduct(id, ctx, db, ct));

        app.MapGet("/admin/plans", (HttpContext ctx, AppDb db, CancellationToken ct) => ListPlans(ctx, db, ct));
        app.MapPost("/admin/plans", (HttpContext ctx, AppDb db, CancellationToken ct) => CreatePlan(ctx, db, ct));
        app.MapPut("/admin/plans/{id:int}", (int id, HttpContext ctx, AppDb db, CancellationToken ct) => UpdatePlan(id, ctx, db, ct));
        app.MapDelete("/admin/plans/{id:int}", (int id, HttpContext ctx, AppDb db, CancellationToken ct) => DeletePlan(id, ctx, db, ct));

        app.MapGet("/v1/app/update-check", (HttpContext ctx, AppDb db, CancellationToken ct) => UpdateCheck(ctx, db, skewSeconds, ct));
    }

    // ---------- Brands ----------

    private static async Task<IResult> ListBrands(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var p = await Require(ctx, db, Permissions.ProductRead, ct);
        if (p.Result is not null) return p.Result;

        var q = db.Brands.AsNoTracking().AsQueryable();
        var search = ctx.Request.Query["q"].ToString().Trim().ToLowerInvariant();
        if (search.Length > 0)
        {
            q = q.Where(b =>
                b.Name.ToLower().Contains(search)
                || b.Slug.Contains(search)
                || b.WebsiteUrl.ToLower().Contains(search)
                || b.SupportEmail.ToLower().Contains(search)
                || b.LogoUrl.ToLower().Contains(search));
        }

        var status = ctx.Request.Query["status"].ToString().Trim().ToLowerInvariant();
        if (status == "active") q = q.Where(b => b.IsActive);
        else if (status == "inactive") q = q.Where(b => !b.IsActive);

        if (DateOnly.TryParse(ctx.Request.Query["from"], out var fromDay))
        {
            var fromUtc = fromDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(b => b.CreatedAt >= fromUtc);
        }
        if (DateOnly.TryParse(ctx.Request.Query["to"], out var toDay))
        {
            var toExclusive = toDay.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(b => b.CreatedAt < toExclusive);
        }

        var projected = q.Select(b => new
        {
            Brand = b,
            ProductCount = db.Products.Count(prod => prod.BrandId == b.Id)
        });

        var sort = ctx.Request.Query["sort"].ToString().Trim().ToLowerInvariant();
        var asc = ctx.Request.Query["dir"].ToString().Trim().Equals("asc", StringComparison.OrdinalIgnoreCase);
        projected = sort switch
        {
            "name" => asc ? projected.OrderBy(x => x.Brand.Name) : projected.OrderByDescending(x => x.Brand.Name),
            "slug" => asc ? projected.OrderBy(x => x.Brand.Slug) : projected.OrderByDescending(x => x.Brand.Slug),
            "websiteurl" => asc ? projected.OrderBy(x => x.Brand.WebsiteUrl) : projected.OrderByDescending(x => x.Brand.WebsiteUrl),
            "products" => asc ? projected.OrderBy(x => x.ProductCount) : projected.OrderByDescending(x => x.ProductCount),
            "status" => asc ? projected.OrderBy(x => x.Brand.IsActive) : projected.OrderByDescending(x => x.Brand.IsActive),
            _ => asc ? projected.OrderBy(x => x.Brand.CreatedAt) : projected.OrderByDescending(x => x.Brand.CreatedAt)
        };

        var total = await projected.CountAsync(ct);
        var limit = CatalogPaging.NormalizeLimit(ctx.Request.Query["limit"].ToString());
        var page = CatalogPaging.ClampPage(CatalogPaging.NormalizePage(ctx.Request.Query["page"].ToString()), total, limit);
        var rows = await projected.Skip((page - 1) * limit).Take(limit).ToListAsync(ct);

        var allCount = await db.Brands.CountAsync(ct);
        var activeCount = await db.Brands.CountAsync(b => b.IsActive, ct);

        return Json(200, new
        {
            brands = rows.Select(x => BrandView(x.Brand, x.ProductCount)),
            total,
            page,
            limit,
            counts = new { total = allCount, active = activeCount, inactive = allCount - activeCount }
        });
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
        if (name.Length < 2) return Fail(400, "name too short");
        if (name.Length > 200) return Fail(400, "name too long");
        if (await db.Brands.AnyAsync(b => b.Slug == slug, ct)) return Fail(409, "slug already exists");

        if (!TryBrandContact(body.Value, out var supportEmail, out var websiteUrl, out var logoUrl, out var contactError))
            return contactError!;

        var brand = new Brand
        {
            Slug = slug,
            Name = name,
            SupportEmail = supportEmail,
            WebsiteUrl = websiteUrl,
            LogoUrl = logoUrl,
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

        if (Str(body.Value, "name") is { Length: > 0 } name)
        {
            if (name.Length < 2) return Fail(400, "name too short");
            if (name.Length > 200) return Fail(400, "name too long");
            brand.Name = name;
        }
        if (!TryBrandContact(body.Value, requirePresent: false, out var supportEmail, out var websiteUrl, out var logoUrl, out var contactError))
            return contactError!;
        if (body.Value.TryGetProperty("supportEmail", out _)) brand.SupportEmail = supportEmail;
        if (body.Value.TryGetProperty("websiteUrl", out _)) brand.WebsiteUrl = websiteUrl;
        if (body.Value.TryGetProperty("logoUrl", out _)) brand.LogoUrl = logoUrl;
        if (TryBool(body.Value, "isActive", out var active)) brand.IsActive = active;

        Audit.Record(db, ctx, p.Principal, "brand.update", brand.Slug);
        await db.SaveChangesAsync(ct);
        return Json(200, new { brand = BrandView(brand) });
    }

    private static async Task<IResult> DeleteBrand(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var p = await Require(ctx, db, Permissions.ProductWrite, ct);
        if (p.Result is not null) return p.Result;

        var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (brand is null) return Fail(404, "brand not found");

        db.Brands.Remove(brand);
        Audit.Record(db, ctx, p.Principal, "brand.delete", brand.Slug);
        await db.SaveChangesAsync(ct);
        return Json(200, new { ok = true, id });
    }

    // ---------- Products ----------

    private static async Task<IResult> ListProducts(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var p = await Require(ctx, db, Permissions.ProductRead, ct);
        if (p.Result is not null) return p.Result;

        var q = db.Products.AsNoTracking().AsQueryable();
        var search = ctx.Request.Query["q"].ToString().Trim().ToLowerInvariant();
        if (search.Length > 0)
        {
            q = q.Where(prod =>
                prod.DisplayName.ToLower().Contains(search)
                || prod.Slug.Contains(search)
                || prod.AppKey.ToLower().Contains(search)
                || prod.KeyPrefix.ToLower().Contains(search)
                || prod.LatestVersion.ToLower().Contains(search)
                || (prod.Brand != null && prod.Brand.Name.ToLower().Contains(search)));
        }

        var status = ctx.Request.Query["status"].ToString().Trim().ToLowerInvariant();
        if (status == "active") q = q.Where(prod => prod.IsActive);
        else if (status == "inactive") q = q.Where(prod => !prod.IsActive);

        if (int.TryParse(ctx.Request.Query["brandId"], out var brandId) && brandId > 0)
            q = q.Where(prod => prod.BrandId == brandId);

        if (DateOnly.TryParse(ctx.Request.Query["from"], out var fromDay))
        {
            var fromUtc = fromDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(prod => prod.CreatedAt >= fromUtc);
        }
        if (DateOnly.TryParse(ctx.Request.Query["to"], out var toDay))
        {
            var toExclusive = toDay.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(prod => prod.CreatedAt < toExclusive);
        }

        var projected = q.Select(prod => new
        {
            Product = prod,
            BrandName = prod.Brand != null ? prod.Brand.Name : "",
            BrandLogoUrl = prod.Brand != null ? prod.Brand.LogoUrl : "",
            LicenseCount = db.Licenses.Count(l => l.ProductId == prod.Id)
        });

        var sort = ctx.Request.Query["sort"].ToString().Trim().ToLowerInvariant();
        var asc = ctx.Request.Query["dir"].ToString().Trim().Equals("asc", StringComparison.OrdinalIgnoreCase);
        projected = sort switch
        {
            "name" => asc ? projected.OrderBy(x => x.Product.DisplayName) : projected.OrderByDescending(x => x.Product.DisplayName),
            "slug" => asc ? projected.OrderBy(x => x.Product.Slug) : projected.OrderByDescending(x => x.Product.Slug),
            "website" => asc ? projected.OrderBy(x => x.BrandName) : projected.OrderByDescending(x => x.BrandName),
            "appkey" => asc ? projected.OrderBy(x => x.Product.AppKey) : projected.OrderByDescending(x => x.Product.AppKey),
            "version" => asc ? projected.OrderBy(x => x.Product.LatestVersion) : projected.OrderByDescending(x => x.Product.LatestVersion),
            "status" => asc ? projected.OrderBy(x => x.Product.IsActive) : projected.OrderByDescending(x => x.Product.IsActive),
            _ => asc ? projected.OrderBy(x => x.Product.CreatedAt) : projected.OrderByDescending(x => x.Product.CreatedAt)
        };

        var total = await projected.CountAsync(ct);
        var limit = CatalogPaging.NormalizeLimit(ctx.Request.Query["limit"].ToString());
        var page = CatalogPaging.ClampPage(CatalogPaging.NormalizePage(ctx.Request.Query["page"].ToString()), total, limit);
        var rows = await projected.Skip((page - 1) * limit).Take(limit).ToListAsync(ct);

        var allCount = await db.Products.CountAsync(ct);
        var activeCount = await db.Products.CountAsync(prod => prod.IsActive, ct);

        return Json(200, new
        {
            products = rows.Select(x => ProductView(x.Product, x.BrandName, x.BrandLogoUrl, x.LicenseCount)),
            total,
            page,
            limit,
            counts = new { total = allCount, active = activeCount, inactive = allCount - activeCount }
        });
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
        var keyPrefix = Str(body.Value, "keyPrefix");
        var displayName = Str(body.Value, "displayName");

        if (!IsSlug(slug)) return Fail(400, "slug must be 2-64 chars of a-z, 0-9, hyphen");
        if (string.IsNullOrWhiteSpace(displayName)) return Fail(400, "missing displayName");
        if (displayName.Length < 2) return Fail(400, "displayName too short");
        if (displayName.Length > 200) return Fail(400, "displayName too long");
        if (string.IsNullOrWhiteSpace(appKey)) return Fail(400, "missing appKey");
        if (appKey.Length > 128) return Fail(400, "appKey too long");
        if (string.IsNullOrWhiteSpace(userAgentPrefix)) return Fail(400, "missing userAgentPrefix");
        if (await db.Products.AnyAsync(x => x.Slug == slug, ct)) return Fail(409, "slug already exists");
        if (await db.Products.AnyAsync(x => x.AppKey == appKey, ct)) return Fail(409, "appKey already exists");

        if (!TryInt(body.Value, "brandId", out var bid) || bid <= 0)
            return Fail(400, "missing brandId");
        if (!await db.Brands.AnyAsync(b => b.Id == bid, ct)) return Fail(422, "unknown brandId");

        var product = new Product
        {
            BrandId = bid,
            Slug = slug,
            DisplayName = displayName,
            AppKey = appKey,
            KeyPrefix = keyPrefix,
            UserAgentPrefix = userAgentPrefix,
            HmacSecret = NewHmacSecret(),
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
        if (Str(body.Value, "displayName") is { Length: > 0 } dn)
        {
            if (dn.Length < 2) return Fail(400, "displayName too short");
            if (dn.Length > 200) return Fail(400, "displayName too long");
            product.DisplayName = dn;
            changes.Add("displayName");
        }
        if (body.Value.TryGetProperty("keyPrefix", out _)) { product.KeyPrefix = Str(body.Value, "keyPrefix"); changes.Add("keyPrefix"); }
        if (Str(body.Value, "userAgentPrefix") is { Length: > 0 } ua) { product.UserAgentPrefix = ua; changes.Add("userAgentPrefix"); }
        if (body.Value.TryGetProperty("latestVersion", out _)) { product.LatestVersion = Str(body.Value, "latestVersion"); changes.Add("latestVersion"); }
        if (body.Value.TryGetProperty("downloadUrl", out _)) { product.DownloadUrl = NullIfEmpty(Str(body.Value, "downloadUrl")); changes.Add("downloadUrl"); }
        if (body.Value.TryGetProperty("releaseNotes", out _)) { product.ReleaseNotes = NullIfEmpty(Str(body.Value, "releaseNotes")); changes.Add("releaseNotes"); }
        if (TryBool(body.Value, "isActive", out var active)) { product.IsActive = active; changes.Add($"isActive={active}"); }
        if (body.Value.TryGetProperty("brandId", out _))
        {
            if (!TryInt(body.Value, "brandId", out var bid) || bid <= 0)
                return Fail(400, "missing brandId");
            if (!await db.Brands.AnyAsync(b => b.Id == bid, ct)) return Fail(422, "unknown brandId");
            product.BrandId = bid;
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

    private static async Task<IResult> DeleteProduct(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var p = await Require(ctx, db, Permissions.ProductWrite, ct);
        if (p.Result is not null) return p.Result;

        var product = await db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (product is null) return Fail(404, "product not found");
        if (await db.Licenses.AnyAsync(l => l.ProductId == id, ct))
            return Fail(409, "product has licenses");
        if (await db.KeyBatches.AnyAsync(b => b.ProductId == id, ct))
            return Fail(409, "product has key batches");

        db.Products.Remove(product);
        Audit.Record(db, ctx, p.Principal, "product.delete", product.Slug);
        await db.SaveChangesAsync(ct);
        return Json(200, new { ok = true, id });
    }

    // ---------- Plans ----------

    private static async Task<IResult> ListPlans(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var p = await Require(ctx, db, Permissions.ProductRead, ct);
        if (p.Result is not null) return p.Result;

        var q = db.Plans.AsNoTracking().AsQueryable();
        var search = ctx.Request.Query["q"].ToString().Trim().ToLowerInvariant();
        if (search.Length > 0)
        {
            q = q.Where(plan =>
                plan.DisplayName.ToLower().Contains(search)
                || plan.Code.Contains(search)
                || plan.Currency.ToLower().Contains(search));
        }

        var status = ctx.Request.Query["status"].ToString().Trim().ToLowerInvariant();
        if (status == "active") q = q.Where(plan => plan.IsActive);
        else if (status == "inactive") q = q.Where(plan => !plan.IsActive);

        var currency = ctx.Request.Query["currency"].ToString().Trim().ToUpperInvariant();
        if (currency.Length == 3) q = q.Where(plan => plan.Currency == currency);

        var duration = ctx.Request.Query["duration"].ToString().Trim().ToLowerInvariant();
        if (duration is "monthly" or "1year" or "2year" or "3year" or "5year" or "lifetime")
            q = q.Where(plan => plan.DurationDays == PlanCatalog.DefaultDurationDays(duration));

        if (DateOnly.TryParse(ctx.Request.Query["from"], out var fromDay))
        {
            var fromUtc = fromDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(plan => plan.CreatedAt >= fromUtc);
        }
        if (DateOnly.TryParse(ctx.Request.Query["to"], out var toDay))
        {
            var toExclusive = toDay.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(plan => plan.CreatedAt < toExclusive);
        }

        var projected = q.Select(plan => new
        {
            Plan = plan,
            LicenseCount = db.Licenses.Count(l => l.PlanType.ToLower() == plan.Code.ToLower())
        });

        var sort = ctx.Request.Query["sort"].ToString().Trim().ToLowerInvariant();
        var asc = ctx.Request.Query["dir"].ToString().Trim().Equals("asc", StringComparison.OrdinalIgnoreCase);
        projected = sort switch
        {
            "name" => asc ? projected.OrderBy(x => x.Plan.DisplayName) : projected.OrderByDescending(x => x.Plan.DisplayName),
            "code" => asc ? projected.OrderBy(x => x.Plan.Code) : projected.OrderByDescending(x => x.Plan.Code),
            "duration" => asc ? projected.OrderBy(x => x.Plan.DurationDays) : projected.OrderByDescending(x => x.Plan.DurationDays),
            "machines" => asc ? projected.OrderBy(x => x.Plan.DefaultMaxMachines) : projected.OrderByDescending(x => x.Plan.DefaultMaxMachines),
            "price" => asc ? projected.OrderBy(x => x.Plan.Price) : projected.OrderByDescending(x => x.Plan.Price),
            "currency" => asc ? projected.OrderBy(x => x.Plan.Currency) : projected.OrderByDescending(x => x.Plan.Currency),
            "status" => asc ? projected.OrderBy(x => x.Plan.IsActive) : projected.OrderByDescending(x => x.Plan.IsActive),
            _ => asc ? projected.OrderBy(x => x.Plan.CreatedAt) : projected.OrderByDescending(x => x.Plan.CreatedAt)
        };

        var total = await projected.CountAsync(ct);
        var limit = CatalogPaging.NormalizeLimit(ctx.Request.Query["limit"].ToString());
        var page = CatalogPaging.ClampPage(CatalogPaging.NormalizePage(ctx.Request.Query["page"].ToString()), total, limit);
        var rows = await projected.Skip((page - 1) * limit).Take(limit).ToListAsync(ct);

        var allCount = await db.Plans.CountAsync(ct);
        var activeCount = await db.Plans.CountAsync(plan => plan.IsActive, ct);
        var currencies = await db.Plans
            .Where(plan => plan.Currency != "")
            .Select(plan => plan.Currency)
            .Distinct()
            .OrderBy(code => code)
            .ToListAsync(ct);

        return Json(200, new
        {
            plans = rows.Select(x => PlanView(x.Plan, x.LicenseCount)),
            total,
            page,
            limit,
            counts = new { total = allCount, active = activeCount, inactive = allCount - activeCount },
            currencies
        });
    }

    private static async Task<IResult> CreatePlan(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var p = await Require(ctx, db, Permissions.PlanWrite, ct);
        if (p.Result is not null) return p.Result;

        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");

        var code = Str(body.Value, "code").ToLowerInvariant();
        var displayName = Str(body.Value, "displayName");
        var currency = Str(body.Value, "currency").ToUpperInvariant();
        if (!IsPlanCode(code)) return Fail(400, "code must be 2-32 chars of a-z, 0-9, hyphen");
        if (string.IsNullOrWhiteSpace(displayName)) return Fail(400, "missing displayName");
        if (displayName.Length < 2) return Fail(400, "displayName too short");
        if (displayName.Length > 200) return Fail(400, "displayName too long");
        if (!TryInt(body.Value, "durationDays", out var durationDays) || durationDays < 1 || durationDays > 36500)
            return Fail(400, "durationDays must be 1-36500");
        if (!IsCurrency(currency)) return Fail(400, "currency must be a 3-letter code");
        if (await db.Plans.AnyAsync(x => x.Code == code, ct)) return Fail(409, "code already exists");

        var maxMachines = TryInt(body.Value, "defaultMaxMachines", out var mm) ? Math.Clamp(mm, 1, 100) : 3;
        if (TryDecimal(body.Value, "price", out var price))
        {
            if (price < 0m) return Fail(400, "price must be zero or greater");
            if (price > 99_999_999.99m) return Fail(400, "price too large");
        }
        else price = 0m;

        var plan = new Plan
        {
            Code = code,
            DisplayName = displayName,
            DurationDays = durationDays,
            DefaultMaxMachines = maxMachines,
            Price = decimal.Round(price, 2, MidpointRounding.AwayFromZero),
            Currency = currency,
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

        var changes = new List<string>();
        if (Str(body.Value, "displayName") is { Length: > 0 } dn)
        {
            if (dn.Length < 2) return Fail(400, "displayName too short");
            if (dn.Length > 200) return Fail(400, "displayName too long");
            plan.DisplayName = dn;
            changes.Add("displayName");
        }
        if (body.Value.TryGetProperty("durationDays", out _))
        {
            if (!TryInt(body.Value, "durationDays", out var dd) || dd < 1 || dd > 36500)
                return Fail(400, "durationDays must be 1-36500");
            plan.DurationDays = dd;
            changes.Add("durationDays");
        }
        if (TryInt(body.Value, "defaultMaxMachines", out var mm))
        {
            plan.DefaultMaxMachines = Math.Clamp(mm, 1, 100);
            changes.Add("defaultMaxMachines");
        }
        if (body.Value.TryGetProperty("price", out _))
        {
            if (!TryDecimal(body.Value, "price", out var price) || price < 0m)
                return Fail(400, "price must be zero or greater");
            if (price > 99_999_999.99m) return Fail(400, "price too large");
            plan.Price = decimal.Round(price, 2, MidpointRounding.AwayFromZero);
            changes.Add("price");
        }
        if (body.Value.TryGetProperty("currency", out _))
        {
            var currency = Str(body.Value, "currency").ToUpperInvariant();
            if (!IsCurrency(currency)) return Fail(400, "currency must be a 3-letter code");
            plan.Currency = currency;
            changes.Add("currency");
        }
        if (TryBool(body.Value, "isActive", out var active))
        {
            plan.IsActive = active;
            changes.Add($"isActive={active}");
        }

        Audit.Record(db, ctx, p.Principal, "plan.update", plan.Code, string.Join(",", changes));
        await db.SaveChangesAsync(ct);
        return Json(200, new { plan = PlanView(plan) });
    }

    private static async Task<IResult> DeletePlan(int id, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var p = await Require(ctx, db, Permissions.PlanWrite, ct);
        if (p.Result is not null) return p.Result;

        var plan = await db.Plans.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (plan is null) return Fail(404, "plan not found");
        if (await db.Licenses.AnyAsync(l => l.PlanType.ToLower() == plan.Code.ToLower(), ct))
            return Fail(409, "plan has licenses");
        if (await db.KeyBatches.AnyAsync(b => b.PlanType.ToLower() == plan.Code.ToLower(), ct))
            return Fail(409, "plan has key batches");
        if (await db.Orders.AnyAsync(o => o.PlanType.ToLower() == plan.Code.ToLower(), ct))
            return Fail(409, "plan has orders");
        if (await db.GatewayPlans.AnyAsync(g => g.PlanCode.ToLower() == plan.Code.ToLower(), ct))
            return Fail(409, "plan has gateway maps");

        db.Plans.Remove(plan);
        Audit.Record(db, ctx, p.Principal, "plan.delete", plan.Code);
        await db.SaveChangesAsync(ct);
        return Json(200, new { ok = true, id });
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

    private static object BrandView(Brand b, int productCount = 0) => new
    {
        b.Id, b.Slug, b.Name, b.SupportEmail, b.WebsiteUrl, b.LogoUrl, b.IsActive, productCount,
        createdAt = b.CreatedAt.ToString("o")
    };

    private static object ProductView(Product p, string brandName = "", string brandLogoUrl = "", int licenseCount = 0) => new
    {
        p.Id,
        p.BrandId,
        brandName,
        brandLogoUrl,
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
        licenseCount,
        createdAt = p.CreatedAt.ToString("o")
    };

    private static object PlanView(Plan p, int licenseCount = 0) => new
    {
        p.Id, p.Code, p.DisplayName, p.DurationDays, p.DefaultMaxMachines, p.Price, p.Currency, p.IsActive,
        licenseCount,
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

    private static bool IsPlanCode(string s) =>
        s.Length is >= 2 and <= 32 && s.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static bool IsCurrency(string s) =>
        s.Length == 3 && s.All(c => c is >= 'A' and <= 'Z');

    private static string NewHmacSecret()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool TryBrandContact(
        JsonElement body,
        out string supportEmail,
        out string websiteUrl,
        out string logoUrl,
        out IResult? error) =>
        TryBrandContact(body, requirePresent: true, out supportEmail, out websiteUrl, out logoUrl, out error);

    private static bool TryBrandContact(
        JsonElement body,
        bool requirePresent,
        out string supportEmail,
        out string websiteUrl,
        out string logoUrl,
        out IResult? error)
    {
        supportEmail = "";
        websiteUrl = "";
        logoUrl = "";
        error = null;

        if (requirePresent || body.TryGetProperty("supportEmail", out _))
        {
            var raw = Str(body, "supportEmail");
            if (raw.Length == 0) supportEmail = "";
            else if (raw.Length < 6)
            {
                error = Fail(400, "supportEmail too short");
                return false;
            }
            else if (!EmailAddress.TryNormalize(raw, out supportEmail))
            {
                error = Fail(400, raw.Length > 256 ? "supportEmail too long" : "invalid supportEmail");
                return false;
            }
        }

        if (requirePresent || body.TryGetProperty("websiteUrl", out _))
        {
            websiteUrl = Str(body, "websiteUrl");
            if (!TryHttpUrl(websiteUrl, "websiteUrl", out error))
                return false;
        }

        if (requirePresent || body.TryGetProperty("logoUrl", out _))
        {
            logoUrl = Str(body, "logoUrl");
            if (!TryHttpUrl(logoUrl, "logoUrl", out error))
                return false;
        }

        return true;
    }

    private static bool TryHttpUrl(string s, string field, out IResult? error)
    {
        error = null;
        if (string.IsNullOrEmpty(s)) return true;
        if (s.Length < 10)
        {
            error = Fail(400, $"{field} too short");
            return false;
        }
        if (s.Length > 512)
        {
            error = Fail(400, $"{field} too long");
            return false;
        }
        if (!IsHttpUrl(s))
        {
            error = Fail(400, $"{field} must be an http(s) URL");
            return false;
        }
        return true;
    }

    private static bool IsHttpUrl(string s)
    {
        if (string.IsNullOrEmpty(s)) return true;
        if (s.Length > 512) return false;
        return Uri.TryCreate(s, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(uri.Host);
    }

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
