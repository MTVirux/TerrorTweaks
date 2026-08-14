using System.Collections.Generic;

namespace TerrorTweaks.Tweaks.ListingHelper;

internal sealed class MarketListingAccumulator
{
    // Offerings arrive one packet at a time and Dalamud never says how many are coming, so a
    // short page is the only signal that the last one has landed.
    public const int ListingsPerPage = 10;

    private readonly List<MarketListing> _listings = [];

    private int? _requestId;
    private int? _lastCompletedRequestId;

    public bool Complete { get; private set; }

    public IReadOnlyList<MarketListing> Listings => _listings;

    public bool HasRequest(int requestId) => _requestId == requestId;

    public void Begin()
    {
        // A lookup given up on mid-flight - one that only ever saw full pages - still has to
        // stay recognised, or a straggler from it is adopted as the next lookup's first page.
        if (_requestId is not null)
            _lastCompletedRequestId = _requestId;

        _listings.Clear();
        _requestId = null;
        Complete = false;
    }

    public void AddPage(int requestId, IReadOnlyList<MarketListing> page)
    {
        if (Complete)
            return;

        // The server replays the id of the last answered request when it responds "please wait
        // and try your search again", which would otherwise read as a genuine empty result.
        if (requestId == _lastCompletedRequestId)
            return;

        if (_requestId is null)
            _requestId = requestId;
        else if (requestId != _requestId)
            return;

        foreach (var listing in page)
            _listings.Add(listing);

        // A full page means more may still follow, even when every entry on it gets filtered
        // out later - giving up here is what makes an item's first HQ offer go missing.
        if (page.Count < ListingsPerPage)
            Finish();
    }

    private void Finish()
    {
        Complete = true;
        _lastCompletedRequestId = _requestId;
    }
}
