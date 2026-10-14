using MasterServer.Data;
using MasterServer.Support;
using Xunit;

namespace MasterServer.Tests;

public class TicketRulesTests
{
    [Fact]
    public void Status_and_priority_are_closed_sets()
    {
        Assert.True(TicketRules.IsStatus(TicketStatuses.Open));
        Assert.True(TicketRules.IsStatus(TicketStatuses.Waiting));
        Assert.True(TicketRules.IsStatus(TicketStatuses.Closed));
        Assert.False(TicketRules.IsStatus("resolved"));
        Assert.True(TicketRules.IsPriority(TicketPriorities.Normal));
        Assert.True(TicketRules.IsPriority(TicketPriorities.High));
        Assert.False(TicketRules.IsPriority("urgent"));
    }

    [Fact]
    public void Customer_cannot_reply_when_closed()
    {
        Assert.True(TicketRules.CanCustomerReply(TicketStatuses.Open));
        Assert.True(TicketRules.CanCustomerReply(TicketStatuses.Waiting));
        Assert.False(TicketRules.CanCustomerReply(TicketStatuses.Closed));
    }

    [Fact]
    public void Customer_message_reopens_waiting_but_not_closed()
    {
        Assert.Equal(TicketStatuses.Open, TicketRules.AfterCustomerMessage(TicketStatuses.Waiting));
        Assert.Equal(TicketStatuses.Open, TicketRules.AfterCustomerMessage(TicketStatuses.Open));
        Assert.Equal(TicketStatuses.Closed, TicketRules.AfterCustomerMessage(TicketStatuses.Closed));
    }

    [Fact]
    public void Staff_message_moves_open_to_waiting_but_not_closed()
    {
        Assert.Equal(TicketStatuses.Waiting, TicketRules.AfterStaffMessage(TicketStatuses.Open));
        Assert.Equal(TicketStatuses.Waiting, TicketRules.AfterStaffMessage(TicketStatuses.Waiting));
        Assert.Equal(TicketStatuses.Closed, TicketRules.AfterStaffMessage(TicketStatuses.Closed));
    }

    [Fact]
    public void Subject_and_body_are_trimmed_and_capped()
    {
        Assert.Equal("", TicketRules.NormalizeSubject("   "));
        Assert.Equal(200, TicketRules.NormalizeSubject(new string('s', 250)).Length);
        Assert.Equal("", TicketRules.NormalizeBody(" \n "));
        Assert.Equal(8000, TicketRules.NormalizeBody(new string('b', 9000)).Length);
    }

    [Fact]
    public void Public_id_is_32_hex_and_not_sequential()
    {
        var a = TicketRules.NewPublicId();
        var b = TicketRules.NewPublicId();
        Assert.True(TicketRules.IsPublicId(a));
        Assert.True(TicketRules.IsPublicId(b));
        Assert.NotEqual(a, b);
        Assert.False(TicketRules.IsPublicId("1"));
        Assert.False(TicketRules.IsPublicId(""));
        Assert.False(TicketRules.IsPublicId(new string('g', 32)));
    }
}
