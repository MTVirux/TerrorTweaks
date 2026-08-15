using System.Collections.Generic;

namespace TerrorTweaks.Tweaks.ListingHelper;

internal readonly record struct StackMerge(int From, int To);

// A listing comes out of one bag slot, so stock split over several has to be poured together
// before any of it can go up. One pair at a time: the game moves the items, and the slots are
// read again before the next pair is picked.
internal static class StackMergePlan
{
    public static StackMerge? Next(IReadOnlyList<int> stacks, int wanted, int maxStackSize)
    {
        // No amount of pouring fits more into a slot than the item stacks to.
        if (wanted <= 0 || wanted > maxStackSize || stacks.Count < 2)
            return null;

        var total = 0;
        foreach (var stack in stacks)
        {
            if (stack >= wanted)
                return null;

            total += stack;
        }

        // Merging only rearranges what is there, so stock that is short overall stays short.
        if (total < wanted)
            return null;

        var largest = 0;
        for (var i = 1; i < stacks.Count; i++)
        {
            if (stacks[i] > stacks[largest])
                largest = i;
        }

        var second = -1;
        for (var i = 0; i < stacks.Count; i++)
        {
            if (i != largest && (second < 0 || stacks[i] > stacks[second]))
                second = i;
        }

        return second >= 0 && stacks[second] > 0 ? new StackMerge(second, largest) : null;
    }
}
