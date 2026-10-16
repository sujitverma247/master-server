using MasterServer.Support;
using Xunit;

namespace MasterServer.Tests;

public class TicketNumberTests
{
    [Fact]
    public void Format_is_sequence_then_year()
    {
        Assert.Equal("TIKC-001-2026", TicketNumber.Format(2026, 1));
        Assert.Equal("TIKC-012-2026", TicketNumber.Format(2026, 12));
        Assert.Equal("TIKC-1000-2026", TicketNumber.Format(2026, 1000));
        Assert.True(TicketNumber.IsValid("TIKC-001-2026"));
        Assert.True(TicketNumber.IsValid("tikc-001-2026"));
    }

    [Fact]
    public void Parse_roundtrips()
    {
        Assert.True(TicketNumber.TryParse("TIKC-007-2026", out var year, out var seq));
        Assert.Equal(2026, year);
        Assert.Equal(7, seq);
        Assert.Equal("TIKC-007-2026", TicketNumber.Normalize(" tikc-007-2026 "));
    }

    [Fact]
    public void Rejects_malformed_and_zero_sequence()
    {
        Assert.False(TicketNumber.IsValid("TIKC-1"));
        Assert.False(TicketNumber.IsValid("TIKC-000-2026"));
        Assert.False(TicketNumber.IsValid("TICK-001-2026"));
        Assert.False(TicketNumber.IsValid("TIKC-001-1999"));
        Assert.False(TicketNumber.TryParse("", out _, out _));
    }

    [Fact]
    public void Sequence_out_of_range_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TicketNumber.Format(2026, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TicketNumber.Format(2026, 100000));
        Assert.Throws<ArgumentOutOfRangeException>(() => TicketNumber.Format(1999, 1));
    }
}
