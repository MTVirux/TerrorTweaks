using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace TerrorTweaks;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public HashSet<string> EnabledTweaks { get; set; } = [];

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
