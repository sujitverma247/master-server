using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Licensing;

/// <summary>
/// Resolves how many days a plan lasts. A matching active <see cref="Plan"/> row wins; otherwise
/// the built-in defaults apply, so licenses keep working before any plan catalog is configured.
/// </summary>
public static class PlanCatalog
{
    public static async Task<int> DurationDaysAsync(AppDb db, string planType, CancellationToken ct)
    {
        var code = (planType ?? "").Trim();
        if (code.Length > 0)
        {
            var plan = await db.Plans
                .Where(p => p.IsActive && p.Code.ToLower() == code.ToLower())
                .FirstOrDefaultAsync(ct);
            if (plan is not null && plan.DurationDays > 0)
                return plan.DurationDays;
        }
        return DefaultDurationDays(code);
    }

    public static int DefaultDurationDays(string planType) => planType.Trim().ToLowerInvariant() switch
    {
        "monthly" => 30,
        "1year" => 365,
        "2year" => 730,
        "3year" => 1095,
        "5year" => 1825,
        "lifetime" => 36500,
        _ => 365
    };
}
