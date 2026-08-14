using System;

namespace TerrorTweaks.Tweaks.RetainerPriceUpdate;

[Serializable]
public sealed class RetainerPriceConfig
{
    public int DelayMs { get; set; } = 250;

    public bool IgnoreQuality { get; set; }

    public bool ShowPanel { get; set; } = true;

    public bool DockToSellList { get; set; } = true;

    public int UndercutGil { get; set; } = 1;

    // A market query is a real server request, so lookups are spaced out rather than fired
    // back to back - a throttled request never answers at all.
    public int LookupDelayMs { get; set; } = 3000;
}
