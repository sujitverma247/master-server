using MasterServer.Customers;
using Xunit;

namespace MasterServer.Tests;

public class EmailAddressTests
{
    [Theory]
    [InlineData("Buyer@Example.COM", "buyer@example.com")]
    [InlineData("  a.b+tag@shop.co.uk  ", "a.b+tag@shop.co.uk")]
    public void Normalizes_trim_and_case(string input, string expected)
    {
        Assert.True(EmailAddress.TryNormalize(input, out var email));
        Assert.Equal(expected, email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("missing-at.example.com")]
    [InlineData("no-domain@")]
    [InlineData("@nodomain.com")]
    [InlineData("spaces emma.t@example.net")]
    [InlineData("no-tld@localhost")]
    public void Rejects_invalid(string input)
    {
        Assert.False(EmailAddress.TryNormalize(input, out _));
    }

    [Fact]
    public void Rejects_over_max_length()
    {
        var local = new string('a', 252);
        Assert.False(EmailAddress.TryNormalize($"{local}@x.co", out _));
    }
}
