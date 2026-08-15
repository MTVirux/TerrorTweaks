using System;
using System.Collections.Generic;

namespace TerrorTweaks.Tweaks.ListingHelper;

internal readonly record struct MarketListing(uint PricePerUnit, bool HighQuality, ulong RetainerId);

internal enum UndercutOutcome
{
    Undercut,
    HeldAtOwn,
    NoListings,

    // Never returned from here - the board had nothing, so the price came off Universalis
    // instead. It sits with the rest so a row has one verdict to colour and explain itself by.
    Universalis,
}

internal readonly record struct UndercutResult(UndercutOutcome Outcome, int Price);

internal static class UndercutCalculator
{
    public const int MinPrice = 1;
    public const int MaxPrice = 999_999_999;

    public static UndercutResult Resolve(
        IReadOnlyList<MarketListing> listings,
        bool highQuality,
        bool ignoreQuality,
        int undercutGil,
        IReadOnlySet<ulong> ownRetainerIds)
    {
        if (Lowest(listings, highQuality, ignoreQuality) is not { } best)
            return new UndercutResult(UndercutOutcome.NoListings, 0);

        // Cutting under our own listing walks the price down a little further on every run,
        // so the lowest is held as it stands instead.
        if (ownRetainerIds.Contains(best.RetainerId))
            return new UndercutResult(UndercutOutcome.HeldAtOwn, Clamp(best.PricePerUnit));

        return new UndercutResult(UndercutOutcome.Undercut, Clamp(best.PricePerUnit - (long)Math.Max(0, undercutGil)));
    }

    private static MarketListing? Lowest(IReadOnlyList<MarketListing> listings, bool highQuality, bool ignoreQuality)
    {
        MarketListing? lowest = null;
        foreach (var listing in listings)
        {
            // An item with no competition at its own quality reports nothing rather than
            // falling back to the other tier, which would undercut HQ down to NQ money.
            if (!ignoreQuality && listing.HighQuality != highQuality)
                continue;

            if (lowest is null || listing.PricePerUnit < lowest.Value.PricePerUnit)
                lowest = listing;
        }

        return lowest;
    }

    private static int Clamp(long price) => (int)Math.Clamp(price, MinPrice, MaxPrice);
}
