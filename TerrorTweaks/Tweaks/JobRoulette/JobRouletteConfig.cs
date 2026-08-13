using System;

namespace TerrorTweaks.Tweaks.JobRoulette;

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
