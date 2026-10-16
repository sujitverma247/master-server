using System.Text.RegularExpressions;
using MasterServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Support;

/// <summary>Human ticket ids: TIKC-001-2026 (yearly sequence, then UTC year).</summary>
public static class TicketNumber
{
    public const string Prefix = "TIKC";

    private static readonly Regex Pattern = new(
        @"^TIKC-(\d{3,5})-(20\d{2})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string Format(int year, int sequence)
    {
        if (year is < 2000 or > 2099)
            throw new ArgumentOutOfRangeException(nameof(year));
        if (sequence < 1 || sequence > 99999)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        return $"{Prefix}-{sequence:000}-{year}";
    }

    public static bool TryParse(string? input, out int year, out int sequence)
    {
        year = 0;
        sequence = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var m = Pattern.Match(input.Trim());
        if (!m.Success) return false;
        sequence = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        year = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        return sequence >= 1 && year is >= 2000 and <= 2099;
    }

    public static bool IsValid(string? input) => TryParse(input, out _, out _);

    public static string Normalize(string? input) => (input ?? "").Trim().ToUpperInvariant();

    public static async Task<string> NextAsync(AppDb db, DateTime utc, CancellationToken ct)
    {
        var year = utc.Kind == DateTimeKind.Utc ? utc.Year : DateTime.SpecifyKind(utc, DateTimeKind.Utc).Year;
        var suffix = $"-{year}";
        var ids = await db.Tickets.AsNoTracking()
            .Where(t => t.PublicId.StartsWith(Prefix + "-") && t.PublicId.EndsWith(suffix))
            .Select(t => t.PublicId)
            .ToListAsync(ct);
        var max = 0;
        foreach (var id in ids)
        {
            if (TryParse(id, out var y, out var seq) && y == year && seq > max)
                max = seq;
        }
        return Format(year, max + 1);
    }
}
