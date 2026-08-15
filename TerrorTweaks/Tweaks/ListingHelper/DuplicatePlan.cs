using System;
using System.Collections.Generic;

namespace TerrorTweaks.Tweaks.ListingHelper;

internal enum DuplicateBlock
{
    None,
    MarketFull,
    NotEnoughStock,
}

internal readonly record struct DuplicationPlan(int Copies, DuplicateBlock Block)
{
    public bool CanStart => Block == DuplicateBlock.None && Copies > 0;
}

internal static class DuplicatePlan
{
    // The retainer market container is twenty slots, and every copy needs an empty one to
    // land in.
    public const int MarketSlots = 20;

    public static DuplicationPlan Build(
        IReadOnlyList<int> sourceStacks,
        int stackSize,
        int occupiedSlots,
        int wanted,
        bool canMerge)
    {
        if (occupiedSlots >= MarketSlots)
            return new DuplicationPlan(0, DuplicateBlock.MarketFull);

        if (stackSize <= 0)
            return new DuplicationPlan(0, DuplicateBlock.NotEnoughStock);

        // A copy is put up out of one bag slot, so partial stacks spread over several only count
        // towards one if they can be poured together first - and a short copy would offer
        // something other than what it duplicated.
        var whole = 0;
        if (canMerge)
        {
            var total = 0;
            foreach (var stack in sourceStacks)
                total += stack;

            whole = total / stackSize;
        }
        else
        {
            foreach (var stack in sourceStacks)
                whole += stack / stackSize;
        }

        if (whole == 0)
            return new DuplicationPlan(0, DuplicateBlock.NotEnoughStock);

        var free = MarketSlots - occupiedSlots;
        return new DuplicationPlan(Math.Clamp(wanted, 0, Math.Min(free, whole)), DuplicateBlock.None);
    }
}
