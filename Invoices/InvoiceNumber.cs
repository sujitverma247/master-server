using System.Text.RegularExpressions;

namespace MasterServer.Invoices;

/// <summary>Human invoice numbers: INV-YYYYMMDD-NNNN (UTC day + daily sequence).</summary>
public static class InvoiceNumber
{
    private static readonly Regex Pattern = new(
        @"^INV-(20\d{6})-(\d{4})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Format(DateTime utc, int sequence)
    {
        if (sequence < 1 || sequence > 9999)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        var day = utc.Kind == DateTimeKind.Utc ? utc.Date : DateTime.SpecifyKind(utc.Date, DateTimeKind.Utc);
        return $"INV-{day:yyyyMMdd}-{sequence:0000}";
    }

    public static bool TryParse(string? input, out DateTime day, out int sequence)
    {
        day = default;
        sequence = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var m = Pattern.Match(input.Trim());
        if (!m.Success) return false;
        if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd", null,
                System.Globalization.DateTimeStyles.None, out var parsed))
            return false;
        sequence = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (sequence < 1) return false;
        day = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        return true;
    }

    public static bool IsValid(string? input) => TryParse(input, out _, out _);
}
