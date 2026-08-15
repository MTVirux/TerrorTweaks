using System;

namespace TerrorTweaks.Tweaks.ListingHelper;

[Serializable]
public sealed class ListingHelperConfig
{
    public int DelayMs { get; set; } = 100;

    public bool IgnoreQuality { get; set; }

    public bool ShowPanel { get; set; } = true;

    public bool DockToSellList { get; set; } = true;

    public bool LockSize { get; set; } = true;

    public bool LockPosition { get; set; }

    public int UndercutGil { get; set; } = 1;

    // A copy comes out of one bag slot, so stock split over several has to be poured together
    // first. Turning this off leaves those slots alone and refuses the copy instead.
    public bool MergeSplitStacks { get; set; } = true;

    // A market query is a real server request, so lookups are spaced out rather than fired
    // back to back - a throttled request never answers at all.
    public int LookupDelayMs { get; set; } = 3000;

    // Off by default: it sends the item to a third party web service, which is not something to
    // start doing on somebody's behalf.
    public bool UseUniversalisFallback { get; set; }

    public UniversalisSource UniversalisSource { get; set; } = UniversalisSource.CheapestThenSaleAverage;
}
