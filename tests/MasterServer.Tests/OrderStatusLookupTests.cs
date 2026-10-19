using MasterServer.Data;
using MasterServer.Payments;
using Xunit;

namespace MasterServer.Tests;

public class OrderStatusLookupTests
{
    [Theory]
    [InlineData("cancelled", true)]
    [InlineData("failed", true)]
    [InlineData("declined", true)]
    [InlineData("paid", false)]
    [InlineData("", false)]
    public void Fail_intent_matches_gateway_return_status(string status, bool expected)
    {
        Assert.Equal(expected, OrderStatusLookup.IsFailIntent(status));
    }

    [Theory]
    [InlineData("success", true)]
    [InlineData("paid", true)]
    [InlineData("completed", true)]
    [InlineData("cancelled", false)]
    public void Success_intent_matches_gateway_return_status(string status, bool expected)
    {
        Assert.Equal(expected, OrderStatusLookup.IsSuccessIntent(status));
    }

    [Fact]
    public void Email_plus_success_intent_is_enough_to_query()
    {
        Assert.True(OrderStatusLookup.CanQuery("", "", "", "ada@example.com", "", "success"));
        Assert.True(OrderStatusLookup.CanQuery("", "", "", "ada@example.com", "", ""));
        Assert.False(OrderStatusLookup.CanQuery("", "", "", "", "", ""));
        Assert.True(OrderStatusLookup.CanQuery("", "pay_1", "", "", "", ""));
    }

    [Theory]
    [InlineData(OrderStatuses.Paid, "success")]
    [InlineData(OrderStatuses.Failed, "failed")]
    [InlineData(OrderStatuses.Cancelled, "cancelled")]
    [InlineData(OrderStatuses.Pending, "pending")]
    [InlineData(OrderStatuses.Refunded, "failed")]
    public void Order_status_maps_to_page_outcome(string status, string outcome)
    {
        Assert.Equal(outcome, OrderStatusLookup.OutcomeFromOrderStatus(status));
    }

    [Fact]
    public void Gateway_names_become_payment_method_labels()
    {
        Assert.Equal("Dodo Payments", OrderStatusLookup.PaymentMethod("dodo"));
        Assert.Equal("Freemius", OrderStatusLookup.PaymentMethod("freemius"));
        Assert.Equal("", OrderStatusLookup.PaymentMethod(""));
    }
}
