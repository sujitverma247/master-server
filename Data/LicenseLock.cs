using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MasterServer.Data;

internal static class LicenseLock
{
    public static async Task<License?> LoadAsync(AppDb db, string key, int productId, CancellationToken ct)
    {
        var tx = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A transaction is required to lock a license.");

        var connection = db.Database.GetDbConnection();
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx.GetDbTransaction();
        cmd.CommandText = """
            SELECT "Id"
            FROM "Licenses"
            WHERE "Key" = @key AND "ProductId" = @productId
            FOR UPDATE
            """;

        var keyParam = cmd.CreateParameter();
        keyParam.ParameterName = "key";
        keyParam.Value = key;
        cmd.Parameters.Add(keyParam);

        var productParam = cmd.CreateParameter();
        productParam.ParameterName = "productId";
        productParam.Value = productId;
        cmd.Parameters.Add(productParam);

        var id = await cmd.ExecuteScalarAsync(ct);
        if (id is null || id is DBNull)
            return null;

        return await db.Licenses
            .Include(l => l.Activations)
            .Include(l => l.Customer)
            .FirstAsync(l => l.Id == Convert.ToInt32(id), ct);
    }
}
