using System.Text;
using MasterServer.Payments;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace MasterServer.Tests;

public class DodoGatewayTests
{
    private const string Secret = "whsec_dGVzdC1kb2RvLXNlY3JldA==";

    [Fact]
    public void Requires_external_plan_and_has_no_hosted_start_url()
    {
        var gw = new DodoGateway(Secret);
        Assert.True(gw.RequiresExternalPlan);
        Assert.Equal("dodo", gw.Name);
        Assert.Null(gw.Start(new OrderStartRequest("pub", "a@b.co", "1year", 10m, "EUR", "pdt_1")).CheckoutUrl);
    }

    [Fact]
    public void FromMinorUnits_divides_cents_and_keeps_yen()
    {
        Assert.Equal(49.99m, DodoGateway.FromMinorUnits(4999m, "EUR"));
        Assert.Equal(12.34m, DodoGateway.FromMinorUnits(12.34m, "EUR"));
        Assert.Equal(2500m, DodoGateway.FromMinorUnits(2500m, "JPY"));
    }

    [Fact]
    public void TryVerify_parses_payment_succeeded_buy_link()
    {
        var body = """
            {"business_id":"bus_x","type":"payment.succeeded","timestamp":"2026-10-15T10:30:00Z",
             "data":{
               "payload_type":"Payment",
               "payment_id":"pay_test456",
               "total_amount":4999,
               "currency":"EUR",
               "customer":{"email":"buyer@example.com","name":"Ada","customer_id":"cus_1"},
               "product_cart":[{"product_id":"pdt_acme_1year","quantity":1}],
               "created_at":"2026-10-15T10:30:00Z"
             }}
            """;
        Assert.True(Verify(body, out var envelope));
        Assert.Equal("paid", envelope.Status);
        Assert.Equal("pay_test456", envelope.ExternalPaymentId);
        Assert.Equal("pdt_acme_1year", envelope.ExternalPlanId);
        Assert.Equal("buyer@example.com", envelope.Email);
        Assert.Equal("Ada", envelope.CustomerName);
        Assert.Equal(49.99m, envelope.Amount);
        Assert.Equal("EUR", envelope.Currency);
        Assert.Equal("cus_1", envelope.ExternalCustomerId);
        Assert.Equal(new DateTime(2026, 10, 15, 10, 30, 0, DateTimeKind.Utc), envelope.PaidAt);
    }

    [Fact]
    public void TryVerify_maps_refund_and_ignores_unrelated_events()
    {
        var refund = """
            {"type":"refund.succeeded","data":{"payload_type":"Refund","payment_id":"pay_test456","amount":4999,"currency":"EUR"}}
            """;
        Assert.True(Verify(refund, out var envelope));
        Assert.Equal("refunded", envelope.Status);
        Assert.Equal("pay_test456", envelope.ExternalPaymentId);
        Assert.Equal(49.99m, envelope.Amount);
        Assert.True(Verify("""{"type":"dispute.opened","data":{}}""", out var ignored));
        Assert.Equal("ignored", ignored.Status);
    }

    [Fact]
    public void TryVerify_reads_metadata_publicId()
    {
        var body = """
            {"type":"payment.succeeded","data":{
              "payment_id":"pay_9","total_amount":1000,"currency":"USD",
              "customer":{"email":"a@b.co"},
              "product_id":"pdt_x",
              "metadata":{"publicId":"abc123","locale":"de-DE"}
            }}
            """;
        Assert.True(Verify(body, out var envelope));
        Assert.Equal("abc123", envelope.PublicId);
        Assert.Equal("de-DE", envelope.Locale);
        Assert.Equal(10.00m, envelope.Amount);
    }

    [Fact]
    public void TryVerify_rejects_bad_signature()
    {
        var gw = new DodoGateway(Secret);
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["webhook-id"] = "wh_1";
        ctx.Request.Headers["webhook-timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        ctx.Request.Headers["webhook-signature"] = "v1,not-the-signature";
        Assert.False(gw.TryVerify(ctx.Request, """{"type":"payment.succeeded"}""", out _));
    }

    [Fact]
    public void TryVerify_rejects_empty_secret()
    {
        var body = """{"type":"payment.succeeded","data":{}}""";
        var gw = new DodoGateway("");
        Assert.False(gw.TryVerify(Signed(body).Request, body, out _));
    }

    private static bool Verify(string body, out WebhookEnvelope envelope)
    {
        var gw = new DodoGateway(Secret);
        return gw.TryVerify(Signed(body).Request, body, out envelope);
    }

    private static DefaultHttpContext Signed(string body)
    {
        var id = "wh_test_1";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["webhook-id"] = id;
        ctx.Request.Headers["webhook-timestamp"] = timestamp;
        ctx.Request.Headers["webhook-signature"] = "v1," + DodoGateway.Sign(Secret, id, timestamp, body);
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return ctx;
    }
}
