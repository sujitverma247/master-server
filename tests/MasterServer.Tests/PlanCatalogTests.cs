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
    [InlineData("5year", 1825)]
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
}
