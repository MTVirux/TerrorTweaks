using TerrorTweaks.Tweaks.ListingHelper;

namespace TerrorTweaks.Tests;

public class UndercutCalculatorTests
{
    private static readonly HashSet<ulong> NoOwn = [];

    private static HashSet<ulong> Own(params ulong[] ids) => [.. ids];

    [Fact]
    public void Resolve_UndercutsLowestMatchingQuality()
    {
        MarketListing[] listings =
        [
            new(1000, true, 10),
            new(800, true, 11),
            new(950, true, 12),
        ];

        var result = UndercutCalculator.Resolve(listings, highQuality: true, ignoreQuality: false, 1, NoOwn);

        Assert.Equal(UndercutOutcome.Undercut, result.Outcome);
        Assert.Equal(799, result.Price);
    }

    [Fact]
    public void Resolve_IgnoresCheaperListingOfOtherQuality()
    {
        MarketListing[] listings =
        [
            new(100, false, 10),
            new(800, true, 11),
        ];

        var result = UndercutCalculator.Resolve(listings, highQuality: true, ignoreQuality: false, 1, NoOwn);

        Assert.Equal(UndercutOutcome.Undercut, result.Outcome);
        Assert.Equal(799, result.Price);
    }

    [Fact]
    public void Resolve_IgnoreQuality_TakesCheapestOfAnyQuality()
    {
        MarketListing[] listings =
        [
            new(100, false, 10),
            new(800, true, 11),
        ];

        var result = UndercutCalculator.Resolve(listings, highQuality: true, ignoreQuality: true, 1, NoOwn);

        Assert.Equal(UndercutOutcome.Undercut, result.Outcome);
        Assert.Equal(99, result.Price);
    }

    [Fact]
    public void Resolve_EmptyBoard_ReportsNoListings()
    {
        var result = UndercutCalculator.Resolve([], highQuality: true, ignoreQuality: false, 1, NoOwn);

        Assert.Equal(UndercutOutcome.NoListings, result.Outcome);
        Assert.Equal(0, result.Price);
    }

    [Fact]
    public void Resolve_NoListingOfRequestedQuality_DoesNotFallBack()
    {
        MarketListing[] listings =
        [
            new(100, false, 10),
            new(150, false, 11),
        ];

        var result = UndercutCalculator.Resolve(listings, highQuality: true, ignoreQuality: false, 1, NoOwn);

        Assert.Equal(UndercutOutcome.NoListings, result.Outcome);
        Assert.Equal(0, result.Price);
    }

    [Fact]
    public void Resolve_OwnListingIsLowest_HoldsThePrice()
    {
        MarketListing[] listings =
        [
            new(800, true, 11),
            new(1000, true, 12),
        ];

        var result = UndercutCalculator.Resolve(listings, highQuality: true, ignoreQuality: false, 1, Own(11));

        Assert.Equal(UndercutOutcome.HeldAtOwn, result.Outcome);
        Assert.Equal(800, result.Price);
    }

    [Fact]
    public void Resolve_OwnListingAboveTheLowest_ChangesNothing()
    {
        MarketListing[] listings =
        [
            new(800, true, 11),
            new(1000, true, 12),
        ];

        var result = UndercutCalculator.Resolve(listings, highQuality: true, ignoreQuality: false, 50, Own(12));

        Assert.Equal(UndercutOutcome.Undercut, result.Outcome);
        Assert.Equal(750, result.Price);
    }

    [Fact]
    public void Resolve_FloorsAtMinPrice()
    {
        MarketListing[] listings = [new(1, true, 10)];

        var result = UndercutCalculator.Resolve(listings, highQuality: true, ignoreQuality: false, 5, NoOwn);

        Assert.Equal(UndercutOutcome.Undercut, result.Outcome);
        Assert.Equal(UndercutCalculator.MinPrice, result.Price);
    }

    [Fact]
    public void Resolve_UndercutMatchingTheLowest_FloorsAtMinPrice()
    {
        MarketListing[] listings = [new(10, true, 10)];

        var result = UndercutCalculator.Resolve(listings, highQuality: true, ignoreQuality: false, 10, NoOwn);

        Assert.Equal(1, result.Price);
    }

    [Fact]
    public void Resolve_NegativeUndercut_IsTreatedAsZero()
    {
        MarketListing[] listings = [new(800, true, 10)];

        var result = UndercutCalculator.Resolve(listings, highQuality: true, ignoreQuality: false, -50, NoOwn);

        Assert.Equal(UndercutOutcome.Undercut, result.Outcome);
        Assert.Equal(800, result.Price);
    }

    [Fact]
    public void Resolve_ClampsAtMaxPrice()
    {
        MarketListing[] listings = [new(4_000_000_000, true, 10)];

        var undercut = UndercutCalculator.Resolve(listings, highQuality: true, ignoreQuality: false, 0, NoOwn);
        var held = UndercutCalculator.Resolve(listings, highQuality: true, ignoreQuality: false, 0, Own(10));

        Assert.Equal(UndercutCalculator.MaxPrice, undercut.Price);
        Assert.Equal(UndercutCalculator.MaxPrice, held.Price);
    }
}
