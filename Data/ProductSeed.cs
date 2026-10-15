using MasterServer.Licensing;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Data;

public static class ProductSeed
{
    public static async Task ApplyAsync(AppDb db, IConfiguration config, ILogger logger, CancellationToken ct)
    {
        var appKey = First(config, "PRODUCT_APP_KEY", "Product:AppKey");
        var secret = First(config, "PRODUCT_HMAC_SECRET", "Product:HmacSecret");
        var userAgentPrefix = First(config, "PRODUCT_USER_AGENT_PREFIX", "Product:UserAgentPrefix");
        if (string.IsNullOrWhiteSpace(appKey) || string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(userAgentPrefix))
        {
            logger.LogInformation("No product configuration found. License routes stay inactive until a product row exists.");
            return;
        }

        var slug = First(config, "PRODUCT_SLUG", "Product:Slug");
        if (string.IsNullOrWhiteSpace(slug))
            slug = "default";
        var displayName = First(config, "PRODUCT_DISPLAY_NAME", "Product:DisplayName");
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = "Product";
        var keyPrefix = First(config, "PRODUCT_KEY_PREFIX", "Product:KeyPrefix");

        var product = await db.Products.FirstOrDefaultAsync(p => p.AppKey == appKey, ct);
        if (product is null)
        {
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
            logger.LogInformation("Configured product {Slug}.", product.Slug);
        }
        else
        {
            product.UserAgentPrefix = userAgentPrefix;
            product.HmacSecret = secret;
            product.IsActive = true;
            if (!string.IsNullOrWhiteSpace(keyPrefix))
                product.KeyPrefix = keyPrefix;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Updated credentials for product {Slug}.", product.Slug);
        }

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

    private static string First(IConfiguration config, string envName, string configKey)
    {
        var fromEnv = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim();
        return (config[configKey] ?? "").Trim();
    }
}
