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

    public static int DefaultDurationDays(string planType)
    {
        var code = (planType ?? "").Trim().ToLowerInvariant();
        if (code == "monthly") return 30;
        if (code == "lifetime") return 36500;
        if (code.EndsWith("year", StringComparison.Ordinal)
            && int.TryParse(code[..^4], out var years)
            && years is >= 1 and <= 10)
        {
            return years * 365;
        }

        return 365;
    }
}
