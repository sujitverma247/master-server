using MasterServer.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MasterServer.Security;

public abstract record HmacResult
{
    public sealed record Ok(Product Product) : HmacResult;
    public sealed record Rejected : HmacResult;
}

public static class HmacRequest
{
    public static async Task<HmacResult> AuthenticateAsync(HttpContext ctx, AppDb db, int skewSeconds, CancellationToken ct)
    {
        var userAgent = ctx.Request.Headers.UserAgent.ToString();
        var appKey = ctx.Request.Headers["X-App-Key"].ToString();
        var timestamp = ctx.Request.Headers["X-Timestamp"].ToString();
        var nonce = ctx.Request.Headers["X-Nonce"].ToString();
        var signature = ctx.Request.Headers["X-Signature"].ToString();

        if (string.IsNullOrEmpty(appKey) || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(signature)
            || nonce.Length > 128)
        {
            return new HmacResult.Rejected();
        }

        if (!long.TryParse(timestamp, out var ts))
            return new HmacResult.Rejected();

        var product = await db.Products.FirstOrDefaultAsync(p => p.AppKey == appKey && p.IsActive, ct);
        if (product is null || string.IsNullOrEmpty(product.HmacSecret) || string.IsNullOrEmpty(product.UserAgentPrefix))
            return new HmacResult.Rejected();

        if (!userAgent.StartsWith(product.UserAgentPrefix, StringComparison.Ordinal))
            return new HmacResult.Rejected();

        var serverTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(serverTime - ts) > skewSeconds)
            return new HmacResult.Rejected();

        var path = ctx.Request.Path.Value ?? "";
        var query = ctx.Request.QueryString.Value ?? "";
        if (query.StartsWith('?'))
            query = query[1..];

        if (!HmacSignature.Matches(product.HmacSecret, appKey, timestamp, nonce, path, query, signature))
            return new HmacResult.Rejected();

        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        await db.UsedNonces.Where(n => n.CreatedAt < cutoff).ExecuteDeleteAsync(ct);

        db.UsedNonces.Add(new UsedNonce { Nonce = nonce, CreatedAt = DateTime.UtcNow });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return new HmacResult.Rejected();
        }

        return new HmacResult.Ok(product);
    }
}
