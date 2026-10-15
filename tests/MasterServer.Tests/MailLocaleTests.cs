using MasterServer.Mail;
using Xunit;

namespace MasterServer.Tests;

public class MailLocaleTests
{
    [Theory]
    [InlineData(null, "en")]
    [InlineData("", "en")]
    [InlineData("en", "en")]
    [InlineData("en-US", "en")]
    [InlineData("fr-FR", "en")]
    [InlineData("de", "de")]
    [InlineData("DE", "de")]
    [InlineData("de-DE", "de")]
    [InlineData("de_DE", "de")]
    [InlineData("de-AT", "de")]
    [InlineData("de-DE,de;q=0.9,en;q=0.8", "de")]
    [InlineData("en-US,en;q=0.9", "en")]
    public void Normalize_maps_os_style_language(string? raw, string expected)
    {
        Assert.Equal(expected, MailLocale.Normalize(raw));
    }
}
