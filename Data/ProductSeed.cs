using MasterServer.Licensing;
using MasterServer.Mail;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Data;

public static class ProductSeed
{
    public static async Task ApplyAsync(AppDb db, IConfiguration config, ILogger logger, CancellationToken ct)
    {
        var secret = First(config, "PRODUCT_HMAC_SECRET", "Product:HmacSecret");
        var appKey = First(config, "PRODUCT_APP_KEY", "Product:AppKey");
        var userAgentPrefix = First(config, "PRODUCT_USER_AGENT_PREFIX", "Product:UserAgentPrefix");
        var slug = First(config, "PRODUCT_SLUG", "Product:Slug");
        var displayName = First(config, "PRODUCT_DISPLAY_NAME", "Product:DisplayName");
        var keyPrefix = First(config, "PRODUCT_KEY_PREFIX", "Product:KeyPrefix");

        var product = await ResolveProductAsync(db, appKey, slug, ct);

        if (product is null)
        {
            // Creating a product still needs identity. HMAC-only env cannot invent an app key.
            if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(appKey) || string.IsNullOrWhiteSpace(userAgentPrefix))
            {
                logger.LogInformation(
                    "No product row to update. Set AppKey and User-Agent in Admin, or set PRODUCT_APP_KEY + PRODUCT_USER_AGENT_PREFIX to create one.");
                return;
            }

            if (string.IsNullOrWhiteSpace(slug))
                slug = "default";
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = "Product";

            var slugTaken = await db.Products.AnyAsync(p => p.Slug == slug, ct);
            if (slugTaken)
                throw new InvalidOperationException("PRODUCT_SLUG is already used by a different app key.");

            product = new Product
            {
                Slug = slug,
                DisplayName = displayName,
                AppKey = appKey,
                KeyPrefix = keyPrefix,
                UserAgentPrefix = userAgentPrefix,
                HmacSecret = secret,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Products.Add(product);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Configured product {Slug} from environment.", product.Slug);
        }
        else
        {
            // Product already exists (Admin). AppKey + User-Agent stay as stored there.
            // Env HMAC, if set, is applied so the desktop LICENSE_HMAC_SECRET can match.
            var changed = false;
            if (!string.IsNullOrWhiteSpace(secret) && product.HmacSecret != secret)
            {
                product.HmacSecret = secret;
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(keyPrefix) && product.KeyPrefix != keyPrefix)
            {
                product.KeyPrefix = keyPrefix;
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(displayName) && product.DisplayName != displayName)
            {
                product.DisplayName = displayName;
                changed = true;
            }
            if (!product.IsActive)
            {
                product.IsActive = true;
                changed = true;
            }
            if (changed)
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Updated product {Slug} from environment. AppKey={AppKey} UserAgentPrefix={Prefix} kept from Admin.",
                    product.Slug, product.AppKey, product.UserAgentPrefix);
            }
        }

        await EnsureBrandAsync(db, product, logger, ct);

        var seedKey = First(config, "SEED_LICENSE_KEY", "License:SeedKey");
        if (string.IsNullOrWhiteSpace(seedKey))
            return;

        var exists = await db.Licenses.AnyAsync(l => l.Key == seedKey, ct);
        if (exists)
            return;

        var planType = First(config, "SEED_PLAN_TYPE", "License:SeedPlanType");
        if (string.IsNullOrWhiteSpace(planType))
            planType = "1year";
        var maxMachines = 3;
        var rawMax = First(config, "SEED_MAX_MACHINES", "License:SeedMaxMachines");
        if (int.TryParse(rawMax, out var parsed))
            maxMachines = Math.Clamp(parsed, 1, 100);

        var purchasedAt = DateTime.UtcNow;
        db.Licenses.Add(new License
        {
            Key = seedKey,
            ProductId = product.Id,
            PlanType = planType,
            MaxMachines = maxMachines,
            CreatedAt = purchasedAt,
            ExpiresAt = await LicenseTerm.FromPurchaseAsync(db, planType, purchasedAt, ct),
            IsActive = true,
            IsRevoked = false
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded one license for product {Slug}.", product.Slug);
    }

    /// <summary>
    /// Prefer the Admin product. Env PRODUCT_APP_KEY / PRODUCT_SLUG only disambiguate when several exist.
    /// </summary>
    internal static async Task<Product?> ResolveProductAsync(AppDb db, string appKey, string slug, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(appKey))
            return await db.Products.FirstOrDefaultAsync(p => p.AppKey == appKey, ct);

        if (!string.IsNullOrWhiteSpace(slug))
            return await db.Products.FirstOrDefaultAsync(p => p.Slug == slug, ct);

        var count = await db.Products.CountAsync(ct);
        if (count == 1)
            return await db.Products.FirstAsync(ct);
        return null;
    }

    private static async Task EnsureBrandAsync(AppDb db, Product product, ILogger logger, CancellationToken ct)
    {
        var fromEmail = MailSenderFactory.DefaultFromEmail();
        var fromName = MailSenderFactory.DefaultFromName();
        var website = PublicWebsite();
        if (fromEmail.Length == 0 && fromName is "Support" or "")
            return;

        await db.Entry(product).Reference(p => p.Brand).LoadAsync(ct);
        var brand = product.Brand;
        var changed = false;
        if (brand is null)
        {
            var slug = BrandSlug(fromName.Length > 0 && fromName != "Support" ? fromName : "brand");
            brand = await db.Brands.FirstOrDefaultAsync(b => b.Slug == slug, ct);
            if (brand is null)
            {
                brand = new Brand
                {
                    Slug = slug,
                    Name = fromName.Length > 0 ? fromName : "Support",
                    SupportEmail = fromEmail,
                    WebsiteUrl = website,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                db.Brands.Add(brand);
                await db.SaveChangesAsync(ct);
            }
            product.BrandId = brand.Id;
            product.Brand = brand;
            changed = true;
        }

        if (fromName.Length > 0 && fromName != "Support" && brand.Name != fromName)
        {
            brand.Name = fromName;
            changed = true;
        }
        if (fromEmail.Length > 0 && brand.SupportEmail != fromEmail)
        {
            brand.SupportEmail = fromEmail;
            changed = true;
        }
        if (website.Length > 0 && brand.WebsiteUrl.Length == 0)
        {
            brand.WebsiteUrl = website;
            changed = true;
        }
        if (changed)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Mail brand {Name} support {Email} for product {Slug}.", brand.Name, brand.SupportEmail, product.Slug);
        }
    }

    private static string PublicWebsite()
    {
        var raw = Environment.GetEnvironmentVariable("WEBSITE_ORIGIN") ?? "";
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return part.TrimEnd('/');
        }
        return "";
    }

    private static string BrandSlug(string name)
    {
        var chars = name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        slug = slug.Trim('-');
        return slug.Length > 0 ? slug : "brand";
    }

    private static string First(IConfiguration config, string envName, string configKey)
    {
        var fromEnv = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim();
        return (config[configKey] ?? "").Trim();
    }
}
