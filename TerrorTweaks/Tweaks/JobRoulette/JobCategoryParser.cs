using System;
using System.Collections.Generic;

namespace TerrorTweaks.Tweaks.JobRoulette;

internal static class JobCategoryParser
{
    private static readonly Dictionary<string, JobCategory> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tank"] = JobCategory.Tank,
        ["tanks"] = JobCategory.Tank,
        ["healer"] = JobCategory.Healer,
        ["healers"] = JobCategory.Healer,
        ["heal"] = JobCategory.Healer,
        ["heals"] = JobCategory.Healer,
        ["melee"] = JobCategory.Melee,
        ["caster"] = JobCategory.Caster,
        ["casters"] = JobCategory.Caster,
        ["magic"] = JobCategory.Caster,
        ["magical"] = JobCategory.Caster,
        ["physranged"] = JobCategory.PhysRanged,
        ["ranged"] = JobCategory.PhysRanged,
        ["phys"] = JobCategory.PhysRanged,
        ["pr"] = JobCategory.PhysRanged,
        ["crafter"] = JobCategory.Crafter,
        ["crafters"] = JobCategory.Crafter,
        ["doh"] = JobCategory.Crafter,
        ["gatherer"] = JobCategory.Gatherer,
        ["gatherers"] = JobCategory.Gatherer,
        ["dol"] = JobCategory.Gatherer,
    };

    public static bool TryParse(string? arg, out JobCategory category)
    {
        if (!string.IsNullOrWhiteSpace(arg))
            return Aliases.TryGetValue(arg.Trim(), out category);

        category = default;
        return false;
    }
}
