using MasterServer.Invoices;
using Xunit;

namespace MasterServer.Tests;

public class InvoiceNumberTests
{
    [Fact]
    public void Format_uses_utc_day_and_four_digit_sequence()
    {
        var n = InvoiceNumber.Format(new DateTime(2026, 10, 15, 18, 4, 0, DateTimeKind.Utc), 7);
        Assert.Equal("INV-20261015-0007", n);
        Assert.True(InvoiceNumber.IsValid(n));
    }

    [Fact]
    public void Parse_roundtrips()
    {
        Assert.True(InvoiceNumber.TryParse("INV-20261015-0001", out var day, out var seq));
        Assert.Equal(new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc), day);
        Assert.Equal(1, seq);
    }

    [Fact]
    public void Rejects_malformed_and_zero_sequence()
    {
        Assert.False(InvoiceNumber.IsValid("INV-1"));
        Assert.False(InvoiceNumber.IsValid("INV-20261015-0000"));
        Assert.False(InvoiceNumber.IsValid("20261015-0001"));
        Assert.False(InvoiceNumber.TryParse("", out _, out _));
    }

    [Fact]
    public void Sequence_out_of_range_throws()
    {
        var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Throws<ArgumentOutOfRangeException>(() => InvoiceNumber.Format(day, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => InvoiceNumber.Format(day, 10000));
    }
}
