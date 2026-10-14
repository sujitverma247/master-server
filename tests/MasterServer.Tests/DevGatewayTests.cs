using MasterServer.Payments;
using Xunit;

namespace MasterServer.Tests;

public class DevGatewayTests
{
    private const string Secret = "test-dev-secret";

    [Fact]
    public void Sign_then_verify_roundtrips()
    {
        var sig = DevGateway.Sign(Secret, "evt1", "100", "n1", "paid", "pub1", "paid");
        Assert.True(DevGateway.SignatureMatches(Secret, "evt1", "100", "n1", "paid", "pub1", "paid", sig));
    }

    [Fact]
    public void Verify_rejects_wrong_secret()
    {
        var sig = DevGateway.Sign(Secret, "evt1", "100", "n1", "paid", "pub1", "paid");
        Assert.False(DevGateway.SignatureMatches("other", "evt1", "100", "n1", "paid", "pub1", "paid", sig));
    }

    [Fact]
    public void Verify_rejects_tampered_status()
    {
        var sig = DevGateway.Sign(Secret, "evt1", "100", "n1", "paid", "pub1", "paid");
        Assert.False(DevGateway.SignatureMatches(Secret, "evt1", "100", "n1", "paid", "pub1", "refunded", sig));
    }

    [Fact]
    public void Registry_resolves_dev_and_manual()
    {
        var registry = new GatewayRegistry(
            new IPaymentGateway[] { new DevGateway(Secret), new ManualGateway() },
            DevGateway.GatewayName);
        Assert.NotNull(registry.Resolve("dev"));
        Assert.NotNull(registry.Resolve("manual"));
        Assert.Null(registry.Resolve("unknown"));
        Assert.Equal(DevGateway.GatewayName, registry.Resolve(null)!.Name);
    }

    [Fact]
    public void Manual_checkout_has_no_url()
    {
        var start = new ManualGateway().Start(new OrderStartRequest("pub", "a@b.co", "1year", 10m, "EUR", null));
        Assert.Null(start.CheckoutUrl);
        Assert.False(new ManualGateway().RequiresExternalPlan);
    }
}
