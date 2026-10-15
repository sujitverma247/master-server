using MasterServer.Catalog;
using Xunit;

namespace MasterServer.Tests;

public class CatalogPagingTests
{
    [Theory]
    [InlineData(null, 10)]
    [InlineData("", 10)]
    [InlineData("5", 10)]
    [InlineData("10", 10)]
    [InlineData("20", 20)]
    [InlineData("50", 50)]
    [InlineData("100", 100)]
    [InlineData("200", 10)]
    public void Normalizes_allowed_page_sizes(string? raw, int expected)
    {
        Assert.Equal(expected, CatalogPaging.NormalizeLimit(raw));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("0", 1)]
    [InlineData("-2", 1)]
    [InlineData("3", 3)]
    public void Normalizes_page_number(string? raw, int expected)
    {
        Assert.Equal(expected, CatalogPaging.NormalizePage(raw));
    }

    [Fact]
    public void Clamps_page_to_available_pages()
    {
        Assert.Equal(1, CatalogPaging.ClampPage(1, 0, 10));
        Assert.Equal(3, CatalogPaging.ClampPage(9, 25, 10));
        Assert.Equal(1, CatalogPaging.ClampPage(1, 25, 10));
    }
}
