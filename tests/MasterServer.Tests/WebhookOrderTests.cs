using MasterServer.Data;
using MasterServer.Payments;
using Xunit;

namespace MasterServer.Tests;

public class WebhookOrderTests
{
    [Fact]
    public void ValidateCreate_accepts_buy_link_payload()
    {
        var envelope = BuyLink();
        Assert.Null(WebhookOrder.ValidateCreate(envelope));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void ValidateCreate_rejects_bad_email(string? email)
    {
        var envelope = BuyLink() with { Email = email };
        Assert.Equal("invalid email", WebhookOrder.ValidateCreate(envelope));
    }

    [Fact]
    public void ValidateCreate_requires_gateway_product_id()
    {
        var envelope = BuyLink() with { ExternalPlanId = "" };
        Assert.Equal("missing externalPlanId", WebhookOrder.ValidateCreate(envelope));
    }

    [Fact]
    public void ValidateCreate_requires_gateway_amount()
    {
        var envelope = BuyLink() with { Amount = null };
        Assert.Equal("missing amount", WebhookOrder.ValidateCreate(envelope));
    }

    [Fact]
    public void ValidateCreate_rejects_negative_amount()
    {
        var envelope = BuyLink() with { Amount = -1m };
        Assert.Equal("invalid amount", WebhookOrder.ValidateCreate(envelope));
    }

    [Fact]
    public void ValidateCreate_requires_currency()
    {
        var envelope = BuyLink() with { Currency = "E" };
        Assert.Equal("missing currency", WebhookOrder.ValidateCreate(envelope));
    }

    [Fact]
    public void StampCharge_overwrites_snapshot_with_gateway_amount()
    {
        var order = new Order { Amount = 10m, Currency = "USD" };
        WebhookOrder.StampCharge(order, BuyLink() with { Amount = 49.5m, Currency = "eur" });
        Assert.Equal(49.50m, order.Amount);
        Assert.Equal("EUR", order.Currency);
    }

    private static WebhookEnvelope BuyLink() => new(
        "evt1",
        "paid",
        "",
        "paid",
        "pay_123",
        ExternalPlanId: "pdt_acme_1year",
        Email: "Buyer@Example.COM",
        Amount: 49.00m,
        Currency: "EUR");
}
