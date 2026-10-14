using MasterServer.Customers;
using Xunit;

namespace MasterServer.Tests;

public class PolicyVersionTests
{
    [Theory]
    [InlineData("2026-10-v1", "2026-10-v1")]
    [InlineData("  V2.0  ", "v2.0")]
    public void Normalizes_version(string input, string expected)
    {
        Assert.True(PolicyVersion.TryNormalize(input, out var version));
        Assert.Equal(expected, version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("Has Space")]
    [InlineData("UPPER_UNDERSCORE")]
    [InlineData("-leading")]
    public void Rejects_invalid_version(string input)
    {
        Assert.False(PolicyVersion.TryNormalize(input, out _));
    }

    [Fact]
    public void Seed_default_version_is_valid()
    {
        Assert.True(PolicyVersion.TryNormalize(RefundPolicySeed.DefaultVersion, out var version));
        Assert.Equal(RefundPolicySeed.DefaultVersion, version);
    }
}
