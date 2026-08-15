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
    private bool _answered;

    internal bool Complete => _accumulator.Complete;

    internal IReadOnlyList<MarketListing> Listings => _accumulator.Listings;

    internal long LastPageAt => _lastPageAt;

    // The server has said something about this item. Not the same as having the offers: the
    // sales history lands first and the offers stream in behind it.
    internal bool Answered => _answered;

    internal void Begin(uint baseItemId)
    {
        _itemId = baseItemId;
        _lastPageAt = 0;
        _answered = false;
        _accumulator.Begin();

        if (_listening)
            return;

        Services.MarketBoard.OfferingsReceived += OnOfferingsReceived;
        Services.MarketBoard.HistoryReceived += OnHistoryReceived;
        _listening = true;
    }

    internal void Stop()
    {
        if (_listening)
        {
            Services.MarketBoard.OfferingsReceived -= OnOfferingsReceived;
            Services.MarketBoard.HistoryReceived -= OnHistoryReceived;
            _listening = false;
        }

        _lastPageAt = 0;
        _answered = false;
        _accumulator.Begin();
    }

    // An item nobody is selling gets no offerings packet at all, so its sales history is the
    // only thing that comes back - and unlike an empty offerings page it names its item.
    private void OnHistoryReceived(IMarketBoardHistory history)
    {
        if (ItemIdNormalizer.ToBaseItemId(history.ItemId) == _itemId)
            _answered = true;
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
