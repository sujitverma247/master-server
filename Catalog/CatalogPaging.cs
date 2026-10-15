namespace MasterServer.Catalog;

public static class CatalogPaging
{
    public static readonly int[] Limits = [10, 20, 50, 100];

    public static int NormalizeLimit(string? raw)
    {
        if (int.TryParse(raw, out var limit) && Limits.Contains(limit))
            return limit;
        return 10;
    }

    public static int NormalizePage(string? raw)
    {
        if (int.TryParse(raw, out var page) && page > 0)
            return page;
        return 1;
    }

    public static int ClampPage(int page, int total, int limit)
    {
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)Math.Max(limit, 1)));
        return Math.Clamp(page, 1, pages);
    }
}
