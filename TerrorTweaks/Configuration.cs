using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace TerrorTweaks;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public HashSet<string> EnabledTweaks { get; set; } = [];

    public JobRouletteConfig JobRoulette { get; set; } = new();

    public BulkPurchaseConfig BulkPurchase { get; set; } = new();

    public ClipboardConfig Clipboard { get; set; } = new();

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}

[Serializable]
public sealed class ClipboardConfig
{
    public List<string> SuppressWhenPresent { get; set; } = [];
    public List<string> LearnedEntries { get; set; } = [];
    public HashSet<string> SuppressedMenus { get; set; } = [];
}

[Serializable]
public sealed class BulkPurchaseConfig
{
    public int DelayMs { get; set; } = 100;
    public bool TopUpMode { get; set; }
}

[Serializable]
public sealed class JobRouletteConfig
{
    public bool IncludeTanks { get; set; } = true;
    public bool IncludeHealers { get; set; } = true;
    public bool IncludeMelee { get; set; } = true;
    public bool IncludePhysRanged { get; set; } = true;
    public bool IncludeCasters { get; set; } = true;
    public bool IncludeCrafters { get; set; }
    public bool IncludeGatherers { get; set; }
    public bool IncludeLimited { get; set; }
}
