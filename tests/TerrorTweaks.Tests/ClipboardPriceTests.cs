using TerrorTweaks.Tweaks.ListingHelper;

namespace TerrorTweaks.Tests;

public class ClipboardPriceTests
{
    [Theory]
    [InlineData("12345", 12345)]
    [InlineData("12,345", 12345)]
    [InlineData("12.345", 12345)]
    [InlineData("12 345", 12345)]
    [InlineData("  12345\r\n", 12345)]
    [InlineData("1,234,567", 1234567)]
    [InlineData("12,345 gil", 12345)]
    [InlineData("12345.00", 12345)]
    [InlineData("999999999", 999999999)]
    [InlineData("1", 1)]
    public void TryParse_Accepts(string text, int expected)
    {
        Assert.True(ClipboardPrice.TryParse(text, out var price));
        Assert.Equal(expected, price);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-500")]
    [InlineData("1000000000")]
    [InlineData("Price: 500")]
    [InlineData("1234,56")]
    [InlineData("12345 gil 99")]
    public void TryParse_Rejects(string? text)
    {
        Assert.False(ClipboardPrice.TryParse(text, out var price));
        Assert.Equal(0, price);
    }

    // A shorthand multiplier would be guesswork in the wrong direction: reading "1.5m" as 1
    // would list the item for a single gil.
    [Theory]
    [InlineData("1.5m")]
    [InlineData("1.5M")]
    [InlineData("1500k")]
    [InlineData("1500K")]
    public void TryParse_RejectsShorthandMultipliers(string text)
        => Assert.False(ClipboardPrice.TryParse(text, out _));
}
