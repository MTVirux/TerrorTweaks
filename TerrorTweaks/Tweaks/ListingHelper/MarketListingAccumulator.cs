using System.Collections.Generic;

namespace TerrorTweaks.Tweaks.ListingHelper;

internal sealed class MarketListingAccumulator
{
    // Offerings arrive one packet at a time and Dalamud never says how many are coming, so a
    // short page is the only signal that the last one has landed.
    public const int ListingsPerPage = 10;

    private readonly List<MarketListing> _listings = [];
    private readonly HashSet<int> _seenRequestIds = [];

    private int? _requestId;

    public bool Complete { get; private set; }

    public IReadOnlyList<MarketListing> Listings => _listings;

    public void Begin()
    {
        _listings.Clear();
        _requestId = null;
        Complete = false;
    }

    // A page answering some other lookup still burns its request id, so an empty page following
    // it cannot be taken for this lookup's answer.
    public void Discard(int requestId) => _seenRequestIds.Add(requestId);

    public void AddPage(int requestId, IReadOnlyList<MarketListing> page)
    {
        if (Complete)
            return;

        if (_requestId is null)
        {
            // The server replays the id of the last answered request when it responds "please
            // wait and try your search again", and a straggler from a lookup that was given up
            // on carries its own old id, so only an unseen id can claim this lookup.
            if (!_seenRequestIds.Add(requestId))
                return;

            _requestId = requestId;
        }
        else if (requestId != _requestId)
        {
            return;
        }

        foreach (var listing in page)
            _listings.Add(listing);

        // A full page means more may still follow, even when every entry on it gets filtered
        // out later - giving up here is what makes an item's first HQ offer go missing.
        if (page.Count < ListingsPerPage)
            Complete = true;
    }
}
