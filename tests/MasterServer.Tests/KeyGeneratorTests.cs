using MasterServer.Keys;
using Xunit;

namespace MasterServer.Tests;

public class KeyGeneratorTests
{
    [Fact]
    public void Generated_key_has_prefix_and_four_groups()
    {
        var key = KeyGenerator.Generate("DAV");
        var parts = key.Split('-');
        Assert.Equal(5, parts.Length);
        Assert.Equal("DAV", parts[0]);
        Assert.All(parts[1..], p => Assert.Equal(5, p.Length));
        Assert.True(KeyGenerator.IsValidFormat(key));
    }

    [Fact]
    public void No_prefix_yields_four_groups_only()
    {
        var key = KeyGenerator.Generate("");
        Assert.Equal(4, key.Split('-').Length);
        Assert.True(KeyGenerator.IsValidFormat(key));
    }

    [Fact]
    public void Alphabet_excludes_ambiguous_characters()
    {
        for (var i = 0; i < 200; i++)
        {
            var body = KeyGenerator.Generate("").Replace("-", "");
            Assert.DoesNotContain('0', body);
            Assert.DoesNotContain('O', body);
            Assert.DoesNotContain('1', body);
            Assert.DoesNotContain('I', body);
        }
    }

    [Theory]
    [InlineData("df", "DF")]
    [InlineData("d-a-v", "DAV")]
    [InlineData("abcdef", "ABCD")]
    [InlineData("toolongprefix", "TLNG")]
    [InlineData("0O1I", "")]
    [InlineData("  av  ", "AV")]
    public void Prefix_is_normalized(string input, string expected)
    {
        Assert.Equal(expected, KeyGenerator.Normalize(input));
    }

    [Fact]
    public void Keys_are_practically_unique()
    {
        var set = new HashSet<string>();
        for (var i = 0; i < 1000; i++) set.Add(KeyGenerator.Generate("AV"));
        Assert.Equal(1000, set.Count);
    }

    [Fact]
    public void IsValidFormat_rejects_malformed()
    {
        Assert.False(KeyGenerator.IsValidFormat(""));
        Assert.False(KeyGenerator.IsValidFormat("AV-123"));
        Assert.False(KeyGenerator.IsValidFormat("AV-ABCDE-ABCDE-ABCDE"));
        Assert.False(KeyGenerator.IsValidFormat("AV-ABCD0-ABCDE-ABCDE-ABCDE"));
    }
}
