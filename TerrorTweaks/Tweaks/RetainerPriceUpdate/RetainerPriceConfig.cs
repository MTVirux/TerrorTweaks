using System;

namespace TerrorTweaks.Tweaks.RetainerPriceUpdate;

[Serializable]
public sealed class RetainerPriceConfig
{
    public int DelayMs { get; set; } = 250;
}
