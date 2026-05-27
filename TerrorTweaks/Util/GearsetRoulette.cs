using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrorTweaks.Util;

internal readonly record struct RouletteOptions(
    bool IncludeTanks,
    bool IncludeHealers,
    bool IncludeMelee,
    bool IncludePhysRanged,
    bool IncludeCasters,
    bool IncludeCrafters,
    bool IncludeGatherers,
    bool IncludeLimited);

internal static class GearsetRoulette
{
    // Limited jobs (e.g. Blue Mage) are gated solely by IncludeLimited regardless of
    // their combat role; otherwise eligibility follows the job's category toggle.
    public static bool IsEligible(JobCategory category, bool isLimited, RouletteOptions opts)
    {
        if (isLimited)
            return opts.IncludeLimited;

        return category switch
        {
            JobCategory.Tank       => opts.IncludeTanks,
            JobCategory.Healer     => opts.IncludeHealers,
            JobCategory.Melee      => opts.IncludeMelee,
            JobCategory.PhysRanged => opts.IncludePhysRanged,
            JobCategory.Caster     => opts.IncludeCasters,
            JobCategory.Crafter    => opts.IncludeCrafters,
            JobCategory.Gatherer   => opts.IncludeGatherers,
            _                      => false,
        };
    }

    // Reduces eligible gearsets to one representative slot per job: the lowest slot
    // index for each job id. Ordered by slot index for deterministic selection.
    public static IReadOnlyList<int> FirstPerJob(IEnumerable<(int slotIndex, uint classJobId)> candidates)
    {
        var firstByJob = new Dictionary<uint, int>();
        foreach (var (slotIndex, classJobId) in candidates)
        {
            if (!firstByJob.TryGetValue(classJobId, out var existing) || slotIndex < existing)
                firstByJob[classJobId] = slotIndex;
        }

        return firstByJob.Values.OrderBy(i => i).ToList();
    }

    // Uniform pick over the representative slots; null when the pool is empty.
    public static int? Pick(IReadOnlyList<int> slotIndices, Random random)
        => slotIndices.Count == 0 ? null : slotIndices[random.Next(slotIndices.Count)];
}
