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

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}

[Serializable]
public sealed class JobRouletteConfig
{
    public bool IncludeCrafters { get; set; }
    public bool IncludeGatherers { get; set; }
    public bool IncludeLimited { get; set; }
}
