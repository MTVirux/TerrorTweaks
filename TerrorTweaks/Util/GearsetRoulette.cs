using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrorTweaks.Util;

internal readonly record struct RouletteOptions(
    bool IncludeCrafters,
    bool IncludeGatherers,
    bool IncludeLimited);

internal static class GearsetRoulette
{
    public static bool IsEligible(JobKind kind, bool isLimited, RouletteOptions opts) => kind switch
    {
        JobKind.Crafter  => opts.IncludeCrafters,
        JobKind.Gatherer => opts.IncludeGatherers,
        JobKind.Combat   => !isLimited || opts.IncludeLimited,
        _                => false,
    };

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
