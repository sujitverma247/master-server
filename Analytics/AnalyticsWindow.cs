using System.Globalization;

namespace MasterServer.Analytics;

/// <summary>UTC day windows for the dashboard. Inclusive start, exclusive end.</summary>
public static class AnalyticsWindow
{
    public const int DefaultDays = 7;
    public const int MinDays = 1;
    public const int MaxDays = 366;

    public static int ClampDays(int days) => Math.Clamp(days, MinDays, MaxDays);

    public static (DateTime From, DateTime ToExclusive, DateTime PreviousFrom) Bounds(DateTime utcNow, int days)
    {
        days = ClampDays(days);
        var today = DateTime.SpecifyKind(utcNow.Date, DateTimeKind.Utc);
        var toExclusive = today.AddDays(1);
        var from = today.AddDays(-(days - 1));
        var previousFrom = from.AddDays(-days);
        return (from, toExclusive, previousFrom);
    }

    public static bool TryParseDay(string? input, out DateTime day)
    {
        day = default;
        if (string.IsNullOrWhiteSpace(input)) return false;
        if (!DateTime.TryParseExact(input.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return false;
        day = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
        return true;
    }

    public static string? ValidateCustom(DateTime from, DateTime toInclusive)
    {
        from = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
        toInclusive = DateTime.SpecifyKind(toInclusive.Date, DateTimeKind.Utc);
        if (toInclusive < from) return "to must be on or after from";
        var days = (int)(toInclusive.AddDays(1) - from).TotalDays;
        if (days > MaxDays) return $"range exceeds {MaxDays} days";
        return null;
    }

    public static (DateTime From, DateTime ToExclusive, DateTime PreviousFrom, int Days) Custom(DateTime from, DateTime toInclusive)
    {
        from = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
        var toExclusive = DateTime.SpecifyKind(toInclusive.Date, DateTimeKind.Utc).AddDays(1);
        var days = (int)(toExclusive - from).TotalDays;
        return (from, toExclusive, from.AddDays(-days), days);
    }

    public static IReadOnlyList<DateTime> Days(DateTime from, DateTime toExclusive)
    {
        var list = new List<DateTime>();
        for (var d = from.Date; d < toExclusive; d = d.AddDays(1))
            list.Add(DateTime.SpecifyKind(d, DateTimeKind.Utc));
        return list;
    }

    public static IReadOnlyList<SeriesBucket> Series(DateTime from, DateTime toExclusive)
    {
        from = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
        toExclusive = DateTime.SpecifyKind(toExclusive.Date, DateTimeKind.Utc);
        var span = (int)(toExclusive - from).TotalDays;
        TimeSpan step;
        if (span <= 31) step = TimeSpan.FromDays(1);
        else if (span <= 93) step = TimeSpan.FromDays(7);
        else step = TimeSpan.Zero; // monthly

        var list = new List<SeriesBucket>();
        var cursor = from;
        while (cursor < toExclusive)
        {
            DateTime next;
            string label;
            if (step == TimeSpan.Zero)
            {
                next = cursor.AddMonths(1);
                label = cursor.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            }
            else
            {
                next = cursor.Add(step);
                label = cursor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            if (next > toExclusive) next = toExclusive;
            list.Add(new SeriesBucket(cursor, next, label));
            cursor = next;
        }
        return list;
    }

    public static int[] Bucket(IReadOnlyList<DateTime> timestamps, IReadOnlyList<SeriesBucket> buckets)
    {
        var counts = new int[buckets.Count];
        foreach (var ts in timestamps)
        {
            var utc = ts.Kind == DateTimeKind.Utc ? ts : DateTime.SpecifyKind(ts, DateTimeKind.Utc);
            for (var i = 0; i < buckets.Count; i++)
            {
                if (utc >= buckets[i].Start && utc < buckets[i].EndExclusive)
                {
                    counts[i]++;
                    break;
                }
            }
        }
        return counts;
    }

    public static int Delta(int current, int previous) => current - previous;

    public static int DeltaPct(int current, int previous)
    {
        if (previous == 0) return current > 0 ? 100 : 0;
        return (int)Math.Round((current - previous) * 100.0 / previous, MidpointRounding.AwayFromZero);
    }
}

public sealed record SeriesBucket(DateTime Start, DateTime EndExclusive, string Label);
