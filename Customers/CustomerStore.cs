using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Customers;

public static class CustomerStore
{
    public static async Task<Customer> FindOrCreateAsync(
        AppDb db, string normalizedEmail, string displayName, string source, CancellationToken ct)
    {
        var existing = await db.Customers.FirstOrDefaultAsync(c => c.Email == normalizedEmail, ct);
        if (existing is not null)
        {
            if (displayName.Length > 0 && existing.DisplayName.Length == 0)
            {
                existing.DisplayName = displayName;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            return existing;
        }

        var customer = new Customer
        {
            Email = normalizedEmail,
            DisplayName = displayName,
            Source = source,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        return customer;
    }
}
