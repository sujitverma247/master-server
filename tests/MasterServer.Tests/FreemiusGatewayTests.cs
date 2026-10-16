using System.Text;
using MasterServer.Payments;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace MasterServer.Tests;

public class FreemiusGatewayTests
{
    private const string Secret = "fs-product-secret";

    [Fact]
    public void Requires_external_plan_and_has_no_hosted_start_url()
    {
        var gw = new FreemiusGateway(Secret);
        Assert.True(gw.RequiresExternalPlan);
        Assert.Equal("freemius", gw.Name);
        Assert.Null(gw.Start(new OrderStartRequest("pub", "a@b.co", "1year", 10m, "EUR", "68452")).CheckoutUrl);
    }

    [Fact]
    public void TryVerify_parses_payment_created_buy_link()
    {
        var body = """
            {"id":"evt_fs_1","type":"payment.created",
             "user":{"id":"99","email":"buyer@example.com","first":"Ada","last":"Lovelace","language":"de"},
             "payment":{"id":"pay_1","plan_id":68452,"gross":49.00,"vat":9.31,"currency":"EUR","created":"2026-10-15T04:03:00Z"},
             "license":{"id":"lic_1","plan_id":68452,"expiration":"2027-10-15T00:00:00Z"}}
            """;
        Assert.True(Verify(body, out var envelope));
        Assert.Equal("evt_fs_1", envelope.EventId);
        Assert.Equal("paid", envelope.Status);
        Assert.Equal("pay_1", envelope.ExternalPaymentId);
        Assert.Equal("68452", envelope.ExternalPlanId);
        Assert.Equal("buyer@example.com", envelope.Email);
        Assert.Equal("Ada Lovelace", envelope.CustomerName);
        Assert.Equal(58.31m, envelope.Amount);
        Assert.Equal("EUR", envelope.Currency);
        Assert.Equal("de", envelope.Locale);
        Assert.Equal("99", envelope.ExternalCustomerId);
        Assert.Equal(new DateTime(2026, 10, 15, 4, 3, 0, DateTimeKind.Utc), envelope.PaidAt);
        Assert.Equal(new DateTime(2027, 10, 15, 0, 0, 0, DateTimeKind.Utc), envelope.ExpiresAt);
    }

    [Fact]
    public void TryVerify_reads_objects_wrapper()
    {
        var body = """
            {"id":"evt_fs_2","type":"license.created",
             "objects":{
               "license":{"id":"lic_1","plan_id":"68452"},
               "user":{"email":"buyer@example.com"},
               "payment":{"id":"pay_1","gross":49,"currency":"eur"},
               "subscription":{"id":"sub_9","plan_id":"68452"}
             }}
            """;
        Assert.True(Verify(body, out var envelope));
        Assert.Equal("paid", envelope.Status);
        Assert.Equal("pay_1", envelope.ExternalPaymentId);
        Assert.Equal("68452", envelope.ExternalPlanId);
        Assert.Equal("sub_9", envelope.ExternalSubscriptionId);
        Assert.True(envelope.IsSubscription);
        Assert.Equal(49m, envelope.Amount);
    }

    [Fact]
    public void TryVerify_maps_refund_and_ignores_unrelated_events()
    {
        Assert.True(Verify("""{"id":"r1","type":"payment.refunded","payment":{"id":"pay_1"}}""", out var refund));
        Assert.Equal("refunded", refund.Status);
        Assert.True(Verify("""{"id":"c1","type":"card.created"}""", out var ignored));
        Assert.Equal("ignored", ignored.Status);
    }

    [Fact]
    public void TryVerify_rejects_bad_signature()
    {
        var gw = new FreemiusGateway(Secret);
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Signature"] = "deadbeef";
        Assert.False(gw.TryVerify(ctx.Request, """{"id":"e","type":"payment.created"}""", out _));
    }

    [Fact]
    public void TryVerify_rejects_empty_secret()
    {
        var gw = new FreemiusGateway("");
        var ctx = Signed("""{"id":"e","type":"payment.created"}""");
        Assert.False(gw.TryVerify(ctx.Request, """{"id":"e","type":"payment.created"}""", out _));
    }

    [Fact]
    public void Registry_resolves_freemius_and_dodo()
    {
        var registry = new GatewayRegistry(
            new IPaymentGateway[]
            {
                new DevGateway("x"), new ManualGateway(), new FreemiusGateway(Secret), new DodoGateway("dodo-secret")
            },
            "dev");
        Assert.Equal("freemius", registry.Resolve("freemius")!.Name);
        Assert.Equal("dodo", registry.Resolve("dodo")!.Name);
        Assert.True(registry.Resolve("freemius")!.RequiresExternalPlan);
        Assert.True(registry.Resolve("dodo")!.RequiresExternalPlan);
    }

    private static bool Verify(string body, out WebhookEnvelope envelope)
    {
        var gw = new FreemiusGateway(Secret);
        return gw.TryVerify(Signed(body).Request, body, out envelope);
    }

    private static DefaultHttpContext Signed(string body)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Signature"] = FreemiusGateway.SignHex(Secret, body);
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return ctx;
    }
}
