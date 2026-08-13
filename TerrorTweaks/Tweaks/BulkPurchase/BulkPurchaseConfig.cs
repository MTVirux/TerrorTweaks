using System;

namespace TerrorTweaks.Tweaks.BulkPurchase;

[Serializable]
public sealed class BulkPurchaseConfig
{
    public int DelayMs { get; set; } = 100;
    public bool TopUpMode { get; set; }
}
