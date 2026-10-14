using MasterServer.Security;
using Xunit;

namespace MasterServer.Tests;

public class HmacSignatureTests
{
    [Fact]
    public void Sign_covers_app_key_timestamp_nonce_path_and_raw_query()
    {
        const string secret = "test-secret";
        var signature = HmacSignature.Sign(secret, "app-key", "1710000000", "nonce-1", "/v1/license/status", "key=ABC-123");

        Assert.True(HmacSignature.Matches(secret, "app-key", "1710000000", "nonce-1", "/v1/license/status", "key=ABC-123", signature));
        Assert.False(HmacSignature.Matches(secret, "app-key", "1710000000", "nonce-1", "/v1/license/status", "key=ABC-124", signature));
        Assert.False(HmacSignature.Matches("other-secret", "app-key", "1710000000", "nonce-1", "/v1/license/status", "key=ABC-123", signature));
    }

    [Fact]
    public void Activate_message_uses_an_empty_query()
    {
        const string secret = "test-secret";
        var signature = HmacSignature.Sign(secret, "app-key", "1710000000", "nonce-1", "/v1/license/activate", "");

        Assert.True(HmacSignature.Matches(secret, "app-key", "1710000000", "nonce-1", "/v1/license/activate", "", signature));
        Assert.False(HmacSignature.Matches(secret, "app-key", "1710000000", "nonce-1", "/v1/license/activate", "key=ABC", signature));
    }
}
