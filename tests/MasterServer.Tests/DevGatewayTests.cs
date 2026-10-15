using System.Text;
using MasterServer.Payments;
using Microsoft.AspNetCore.Http;
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

    [Fact]
    public void TryVerify_parses_buy_link_payload_without_publicId()
    {
        var body = """
            {"eventType":"paid","publicId":"","status":"paid","externalPaymentId":"pay_abc",
             "externalPlanId":"pdt_acme_1year","email":"buyer@example.com","amount":49.00,"currency":"EUR",
             "customerName":"Invoice Buyer","paidAt":"2026-10-15T04:03:00Z"}
            """;
        Assert.True(Verify(body, publicId: "", out var envelope));
        Assert.Equal("", envelope.PublicId);
        Assert.Equal("pay_abc", envelope.ExternalPaymentId);
        Assert.Equal("pdt_acme_1year", envelope.ExternalPlanId);
        Assert.Equal("buyer@example.com", envelope.Email);
        Assert.Equal(49.00m, envelope.Amount);
        Assert.Equal("EUR", envelope.Currency);
        Assert.Equal("Invoice Buyer", envelope.CustomerName);
        Assert.Equal(new DateTime(2026, 10, 15, 4, 3, 0, DateTimeKind.Utc), envelope.PaidAt);
        Assert.Null(envelope.Locale);
    }

    [Fact]
    public void TryVerify_parses_locale_lang_or_language()
    {
        var body = """
            {"eventType":"paid","publicId":"","status":"paid","externalPaymentId":"pay_de",
             "externalPlanId":"pdt_acme_1year","email":"buyer@example.com","amount":49.00,"currency":"EUR",
             "locale":"de-DE"}
            """;
        Assert.True(Verify(body, publicId: "", out var envelope));
        Assert.Equal("de-DE", envelope.Locale);
    }

    [Fact]
    public void TryVerify_still_accepts_checkout_publicId()
    {
        var body = """{"eventType":"paid","publicId":"pub1","status":"paid","externalPaymentId":"pay1"}""";
        Assert.True(Verify(body, publicId: "pub1", out var envelope));
        Assert.Equal("pub1", envelope.PublicId);
        Assert.Equal("pay1", envelope.ExternalPaymentId);
        Assert.Null(envelope.Email);
        Assert.Null(envelope.Amount);
    }

    [Fact]
    public void TryVerify_rejects_bad_signature_on_buy_link()
    {
        var gw = new DevGateway(Secret);
        var ctx = SignedContext("paid", "", "paid", "wrong-secret");
        Assert.False(gw.TryVerify(ctx.Request, """{"eventType":"paid","publicId":"","status":"paid"}""", out _));
    }

    private static bool Verify(string body, string publicId, out WebhookEnvelope envelope)
    {
        var gw = new DevGateway(Secret);
        var ctx = SignedContext("paid", publicId, "paid", Secret);
        return gw.TryVerify(ctx.Request, body, out envelope);
    }

    private static DefaultHttpContext SignedContext(string eventType, string publicId, string status, string secret)
    {
        var eventId = "evt-buy";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = "n-buy-1";
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Webhook-Id"] = eventId;
        ctx.Request.Headers["X-Webhook-Timestamp"] = timestamp;
        ctx.Request.Headers["X-Webhook-Nonce"] = nonce;
        ctx.Request.Headers["X-Webhook-Signature"] = DevGateway.Sign(secret, eventId, timestamp, nonce, eventType, publicId, status);
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        return ctx;
    }
}
