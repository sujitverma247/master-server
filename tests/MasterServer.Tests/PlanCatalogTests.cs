using MasterServer.Licensing;
using Xunit;

namespace MasterServer.Tests;

public class PlanCatalogTests
{
    [Theory]
    [InlineData("monthly", 30)]
    [InlineData("1year", 365)]
    [InlineData("2year", 730)]
    [InlineData("3year", 1095)]
    [InlineData("4year", 1460)]
    [InlineData("5year", 1825)]
    [InlineData("6year", 2190)]
    [InlineData("7year", 2555)]
    [InlineData("8year", 2920)]
    [InlineData("9year", 3285)]
    [InlineData("10year", 3650)]
    [InlineData("lifetime", 36500)]
    [InlineData("LIFETIME", 36500)]
    [InlineData(" 1year ", 365)]
    [InlineData("unknown", 365)]
    [InlineData("", 365)]
    public void Default_duration_matches_plan_code(string planType, int expected)
    {
        Assert.Equal(expected, PlanCatalog.DefaultDurationDays(planType));
    }

    [Fact]
    public void LicenseApi_delegates_to_the_default_table()
    {
        Assert.Equal(365, LicenseApi.PlanDurationDays("1year"));
        Assert.Equal(36500, LicenseApi.PlanDurationDays("lifetime"));
    }

    [Fact]
    public void Expiry_is_counted_from_purchase_not_activation()
    {
        var purchased = new DateTime(2026, 10, 16, 1, 41, 0, DateTimeKind.Utc);
        var activated = purchased.AddDays(40);
        var expires = LicenseTerm.FromPurchase(purchased, PlanCatalog.DefaultDurationDays("1year"));

        Assert.Equal(purchased.AddDays(365), expires);
        Assert.NotEqual(activated.AddDays(365), expires);
        Assert.False(LicenseTerm.IsExpired(expires, purchased.AddDays(364)));
        Assert.True(LicenseTerm.IsExpired(expires, purchased.AddDays(366)));
        Assert.False(LicenseTerm.IsExpired(null, purchased));
    }
}
