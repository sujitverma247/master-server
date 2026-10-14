using MasterServer.Payments;
using Xunit;

namespace MasterServer.Tests;

public class IdempotencyKeyTests
{
    [Theory]
    [InlineData("checkout-1")]
    [InlineData("abc.def-12")]
    public void Accepts_valid_keys(string input)
    {
        Assert.True(IdempotencyKey.TryNormalize(input, out var key));
        Assert.Equal(input, key);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("has space xx")]
    [InlineData("bad/slash")]
    [InlineData("")]
    public void Rejects_invalid_keys(string input)
    {
        Assert.False(IdempotencyKey.TryNormalize(input, out _));
    }

    [Fact]
    public void New_tokens_are_unique()
    {
        var a = IdempotencyKey.New();
        var b = IdempotencyKey.New();
        Assert.NotEqual(a, b);
        Assert.True(IdempotencyKey.TryNormalize(a, out _));
    }

    [Fact]
    public void Sha256_is_stable()
    {
        Assert.Equal(IdempotencyKey.Sha256Hex("abc"), IdempotencyKey.Sha256Hex("abc"));
        Assert.NotEqual(IdempotencyKey.Sha256Hex("abc"), IdempotencyKey.Sha256Hex("abd"));
    }
}
