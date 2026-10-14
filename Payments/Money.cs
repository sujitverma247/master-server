namespace MasterServer.Payments;

/// <summary>Money is decimal, two places, never float.</summary>
public static class Money
{
    public static decimal Normalize(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static bool HasAtMostTwoPlaces(decimal amount) =>
        amount == Normalize(amount);

    public static bool IsNonNegative(decimal amount) => amount >= 0m;
}
