using System;
using System.Collections.Generic;

namespace TerrorTweaks.Tweaks.ListingHelper;

internal readonly record struct UniversalisListing(uint PricePerUnit, bool HighQuality, string World);

internal readonly record struct UniversalisSale(uint PricePerUnit, bool HighQuality, string World);

internal sealed record UniversalisItem(
    IReadOnlyList<UniversalisListing> Listings,
    IReadOnlyList<UniversalisSale> Sales);

// Public only because the config serialises it.
public enum UniversalisSource
{
    CheapestListing,
    SaleAverage,
    CheapestThenSaleAverage,
}

internal enum UniversalisBasis
{
    None,
    Listing,
    SaleAverage,
}

internal readonly record struct UniversalisPrice(UniversalisBasis Basis, int Price, string Scope);

// Works out what an item nobody is selling here should be asked for, from what the rest of the
// data centre is doing with it.
internal static class UniversalisFallback
{
    private static readonly UniversalisPrice Nothing = new(UniversalisBasis.None, 0, string.Empty);

    public static UniversalisPrice Resolve(
        UniversalisItem item,
        UniversalisSource source,
        bool highQuality,
        bool ignoreQuality,
        int undercutGil,
        string homeWorld,
        string dataCentre)
    {
        if (source != UniversalisSource.SaleAverage)
        {
            var cheapest = Cheapest(item.Listings, highQuality, ignoreQuality, undercutGil);
            if (cheapest.Basis != UniversalisBasis.None || source == UniversalisSource.CheapestListing)
                return cheapest;
        }

        return Average(item.Sales, highQuality, ignoreQuality, homeWorld, dataCentre);
    }

    // Someone on the data centre is really selling at this, so it is undercut like any other
    // competitor - a traveller can buy theirs instead of ours.
    private static UniversalisPrice Cheapest(
        IReadOnlyList<UniversalisListing> listings,
        bool highQuality,
        bool ignoreQuality,
        int undercutGil)
    {
        UniversalisListing? lowest = null;
        foreach (var listing in listings)
        {
            if (!Wanted(listing.HighQuality, highQuality, ignoreQuality))
                continue;

            if (lowest is null || listing.PricePerUnit < lowest.Value.PricePerUnit)
                lowest = listing;
        }

        if (lowest is not { } best)
            return Nothing;

        var price = Clamp(best.PricePerUnit - (long)Math.Max(0, undercutGil));
        return new UniversalisPrice(UniversalisBasis.Listing, price, best.World);
    }

    // What the item last went for rather than what anyone is asking, so there is nobody to
    // undercut and the average stands as the price.
    private static UniversalisPrice Average(
        IReadOnlyList<UniversalisSale> sales,
        bool highQuality,
        bool ignoreQuality,
        string homeWorld,
        string dataCentre)
    {
        var homeTotal = 0L;
        var homeCount = 0;
        var total = 0L;
        var count = 0;

        foreach (var sale in sales)
        {
            if (!Wanted(sale.HighQuality, highQuality, ignoreQuality))
                continue;

            total += sale.PricePerUnit;
            count++;

            if (!string.Equals(sale.World, homeWorld, StringComparison.OrdinalIgnoreCase))
                continue;

            homeTotal += sale.PricePerUnit;
            homeCount++;
        }

        // Our own world is what the listing has to sell on, so its sales say more than the
        // data centre's - the wider set is only there for an item nobody here has traded.
        if (homeCount > 0)
            return new UniversalisPrice(UniversalisBasis.SaleAverage, Mean(homeTotal, homeCount), homeWorld);

        return count > 0
            ? new UniversalisPrice(UniversalisBasis.SaleAverage, Mean(total, count), dataCentre)
            : Nothing;
    }

    private static bool Wanted(bool listingIsHighQuality, bool highQuality, bool ignoreQuality) =>
        ignoreQuality || listingIsHighQuality == highQuality;

    private static int Mean(long total, int count) =>
        Clamp((long)Math.Round((double)total / count, MidpointRounding.AwayFromZero));

    private static int Clamp(long price) =>
        (int)Math.Clamp(price, UndercutCalculator.MinPrice, UndercutCalculator.MaxPrice);
}
