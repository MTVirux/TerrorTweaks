using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using TerrorTweaks.Tweaks.BulkPurchase;
using TerrorTweaks.Tweaks.JobRoulette;
using TerrorTweaks.Tweaks.RetainerPriceUpdate;

namespace TerrorTweaks;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public HashSet<string> EnabledTweaks { get; set; } = [];

    public JobRouletteConfig JobRoulette { get; set; } = new();

    public BulkPurchaseConfig BulkPurchase { get; set; } = new();

    public RetainerPriceConfig RetainerPrice { get; set; } = new();

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
