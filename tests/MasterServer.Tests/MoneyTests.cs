using MasterServer.Payments;
using Xunit;

namespace MasterServer.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData(9.999, 10.00)]
    [InlineData(9.994, 9.99)]
    [InlineData(49.5, 49.50)]
    [InlineData(0, 0)]
    public void Normalize_rounds_to_two_places(decimal input, decimal expected)
    {
        Assert.Equal(expected, Money.Normalize(input));
    }

    [Fact]
    public void Rejects_more_than_two_places()
    {
        Assert.False(Money.HasAtMostTwoPlaces(1.001m));
        Assert.True(Money.HasAtMostTwoPlaces(1.00m));
    }

    [Fact]
    public void Non_negative()
    {
        Assert.True(Money.IsNonNegative(0m));
        Assert.False(Money.IsNonNegative(-0.01m));
    }
}
