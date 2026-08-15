using TerrorTweaks.Tweaks.ListingHelper;

namespace TerrorTweaks.Tests;

public class MarketListingAccumulatorTests
{
    private static MarketListing[] FullPage(uint firstPrice)
    {
        var page = new MarketListing[MarketListingAccumulator.ListingsPerPage];
        for (var i = 0; i < page.Length; i++)
            page[i] = new MarketListing(firstPrice + (uint)i, true, 10);

        return page;
    }

    [Fact]
    public void AddPage_ShortPage_CompletesAndKeepsListings()
    {
        var accumulator = new MarketListingAccumulator();

        accumulator.AddPage(3, [new(100, true, 10), new(200, false, 11)]);

        Assert.True(accumulator.Complete);
        Assert.Equal(2, accumulator.Listings.Count);
        Assert.Equal(100u, accumulator.Listings[0].PricePerUnit);
        Assert.Equal(200u, accumulator.Listings[1].PricePerUnit);
    }

    [Fact]
    public void AddPage_FullPageThenShortPage_CombinesInOrder()
    {
        var accumulator = new MarketListingAccumulator();

        accumulator.AddPage(3, FullPage(100));

        Assert.False(accumulator.Complete);

        accumulator.AddPage(3, [new(500, true, 11)]);

        Assert.True(accumulator.Complete);
        Assert.Equal(MarketListingAccumulator.ListingsPerPage + 1, accumulator.Listings.Count);
        Assert.Equal(100u, accumulator.Listings[0].PricePerUnit);
        Assert.Equal(109u, accumulator.Listings[9].PricePerUnit);
        Assert.Equal(500u, accumulator.Listings[10].PricePerUnit);
    }

    [Fact]
    public void AddPage_TwoFullPagesThenShortPage_CombinesInOrder()
    {
        var accumulator = new MarketListingAccumulator();

        accumulator.AddPage(3, FullPage(100));
        accumulator.AddPage(3, FullPage(200));

        Assert.False(accumulator.Complete);

        accumulator.AddPage(3, [new(500, true, 11)]);

        Assert.True(accumulator.Complete);
        Assert.Equal(21, accumulator.Listings.Count);
        Assert.Equal(100u, accumulator.Listings[0].PricePerUnit);
        Assert.Equal(200u, accumulator.Listings[10].PricePerUnit);
        Assert.Equal(500u, accumulator.Listings[20].PricePerUnit);
    }

    [Fact]
    public void AddPage_ExactlyFullPage_DoesNotCompleteButKeepsListings()
    {
        var accumulator = new MarketListingAccumulator();

        accumulator.AddPage(3, FullPage(100));

        Assert.False(accumulator.Complete);
        Assert.Equal(MarketListingAccumulator.ListingsPerPage, accumulator.Listings.Count);
        Assert.Equal(100u, accumulator.Listings[0].PricePerUnit);
    }

    [Fact]
    public void AddPage_DifferentRequestId_IsIgnored()
    {
        var accumulator = new MarketListingAccumulator();

        accumulator.AddPage(3, FullPage(100));
        accumulator.AddPage(4, [new(500, true, 11)]);

        Assert.False(accumulator.Complete);
        Assert.Equal(MarketListingAccumulator.ListingsPerPage, accumulator.Listings.Count);

        accumulator.AddPage(3, [new(600, true, 12)]);

        Assert.True(accumulator.Complete);
        Assert.Equal(600u, accumulator.Listings[^1].PricePerUnit);
    }

    [Fact]
    public void AddPage_AfterCompletion_IsIgnored()
    {
        var accumulator = new MarketListingAccumulator();

        accumulator.AddPage(3, [new(100, true, 10)]);
        accumulator.AddPage(3, [new(500, true, 11)]);

        Assert.True(accumulator.Complete);
        Assert.Single(accumulator.Listings);
        Assert.Equal(100u, accumulator.Listings[0].PricePerUnit);
    }

    [Fact]
    public void AddPage_ReplayedRequestIdAfterBegin_IsIgnoredButNewIdIsAccepted()
    {
        var accumulator = new MarketListingAccumulator();

        accumulator.AddPage(3, [new(100, true, 10)]);
        accumulator.Begin();
        accumulator.AddPage(3, []);

        Assert.False(accumulator.Complete);
        Assert.Empty(accumulator.Listings);

        accumulator.AddPage(4, [new(500, true, 11)]);

        Assert.True(accumulator.Complete);
        Assert.Single(accumulator.Listings);
        Assert.Equal(500u, accumulator.Listings[0].PricePerUnit);
    }

    [Fact]
    public void Begin_ClearsListingsAndCompletion()
    {
        var accumulator = new MarketListingAccumulator();

        accumulator.AddPage(3, [new(100, true, 10), new(150, true, 11)]);

        Assert.True(accumulator.Complete);

        accumulator.Begin();

        Assert.False(accumulator.Complete);
        Assert.Empty(accumulator.Listings);
    }

    [Fact]
    public void AddPage_EmptyPageWithFreshId_CompletesWithNoListings()
    {
        var accumulator = new MarketListingAccumulator();

        accumulator.AddPage(3, []);

        Assert.True(accumulator.Complete);
        Assert.Empty(accumulator.Listings);
    }

    [Fact]
    public void AddPage_EmptyPageAfterOwnPages_Completes()
    {
        var accumulator = new MarketListingAccumulator();

        accumulator.AddPage(3, FullPage(100));
        accumulator.AddPage(3, []);

        Assert.True(accumulator.Complete);
        Assert.Equal(MarketListingAccumulator.ListingsPerPage, accumulator.Listings.Count);
    }

    [Fact]
    public void AddPage_EmptyPageWithDiscardedId_IsIgnored()
    {
        var accumulator = new MarketListingAccumulator();

        accumulator.Discard(3);
        accumulator.AddPage(3, []);

        Assert.False(accumulator.Complete);

        accumulator.AddPage(4, []);

        Assert.True(accumulator.Complete);
        Assert.Empty(accumulator.Listings);
    }

    [Fact]
    public void AddPage_EmptyPageReplayingAnAbandonedLookupsId_IsIgnored()
    {
        var accumulator = new MarketListingAccumulator();

        // A lookup that only ever saw full pages and was given up on mid-flight.
        accumulator.AddPage(3, FullPage(100));
        accumulator.Begin();
        accumulator.AddPage(3, []);

        Assert.False(accumulator.Complete);
        Assert.Empty(accumulator.Listings);
    }
}
