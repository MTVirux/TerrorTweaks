using System;
using System.Collections.Generic;
using Dalamud.Game.Network.Structures;
using TerrorTweaks.Util;

namespace TerrorTweaks.Tweaks.ListingHelper;

internal sealed class MarketLookup
{
    private readonly MarketListingAccumulator _accumulator = new();

    private uint _itemId;
    private long _lastPageAt;
    private bool _listening;

    internal bool Complete => _accumulator.Complete;

    internal IReadOnlyList<MarketListing> Listings => _accumulator.Listings;

    internal long LastPageAt => _lastPageAt;

    internal void Begin(uint baseItemId)
    {
        _itemId = baseItemId;
        _lastPageAt = 0;
        _accumulator.Begin();

        if (_listening)
            return;

        Services.MarketBoard.OfferingsReceived += OnOfferingsReceived;
        _listening = true;
    }

    internal void Stop()
    {
        if (_listening)
        {
            Services.MarketBoard.OfferingsReceived -= OnOfferingsReceived;
            _listening = false;
        }

        _lastPageAt = 0;
        _accumulator.Begin();
    }

    private void OnOfferingsReceived(IMarketBoardCurrentOfferings offerings)
    {
        var page = offerings.ItemListings;

        // An empty page carries no item id to check it against, so the request id is all there
        // is to go on: an unseen one is this lookup answering "nobody is selling it", while a
        // replayed one is a throttle notice or a straggler meant for an earlier item.
        if (page.Count == 0)
        {
            _accumulator.AddPage(offerings.RequestId, []);
            return;
        }

        if (ItemIdNormalizer.ToBaseItemId(page[0].ItemId) != _itemId)
        {
            _accumulator.Discard(offerings.RequestId);
            return;
        }

        var listings = new List<MarketListing>(page.Count);
        foreach (var listing in page)
            listings.Add(new MarketListing(listing.PricePerUnit, listing.IsHq, listing.RetainerId));

        _accumulator.AddPage(offerings.RequestId, listings);
        _lastPageAt = Environment.TickCount64;
    }
}
