using MasterServer.Security;
using Xunit;

namespace MasterServer.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_then_verify_roundtrips()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple");
        Assert.True(PasswordHasher.Verify("correct horse battery staple", hash));
    }

    [Fact]
    public void Verify_rejects_wrong_password()
    {
        var hash = PasswordHasher.Hash("s3cret");
        Assert.False(PasswordHasher.Verify("S3cret", hash));
        Assert.False(PasswordHasher.Verify("", hash));
    }

    [Fact]
    public void Hashes_are_salted_and_differ_each_time()
    {
        var a = PasswordHasher.Hash("same");
        var b = PasswordHasher.Hash("same");
        Assert.NotEqual(a, b);
        Assert.True(PasswordHasher.Verify("same", a));
        Assert.True(PasswordHasher.Verify("same", b));
    }

    [Fact]
    public void Verify_rejects_malformed_stored_value()
    {
        Assert.False(PasswordHasher.Verify("x", "not-a-valid-hash"));
        Assert.False(PasswordHasher.Verify("x", "10.notbase64.notbase64"));
    }
}

public class SessionTokenTests
{
    [Fact]
    public void New_tokens_are_unique_and_url_safe()
    {
        var a = SessionToken.New();
        var b = SessionToken.New();
        Assert.NotEqual(a, b);
        Assert.DoesNotContain('+', a);
        Assert.DoesNotContain('/', a);
        Assert.DoesNotContain('=', a);
    }

    [Fact]
    public void Hash_is_stable_and_hides_the_token()
    {
        var token = SessionToken.New();
        Assert.Equal(SessionToken.Hash(token), SessionToken.Hash(token));
        Assert.NotEqual(token, SessionToken.Hash(token));
        Assert.Equal(64, SessionToken.Hash(token).Length);
    }
}
