using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrorTweaks.Framework;

internal static class TweakOrdering
{
    // Names the saved order has never seen go on top, so a tweak added by an update is
    // the first thing in the list rather than buried wherever reflection put it.
    public static List<string> Arrange(IEnumerable<string> saved, IEnumerable<string> present)
    {
        var remaining = new HashSet<string>(present, StringComparer.Ordinal);
        var ordered = new List<string>();

        foreach (var name in saved)
        {
            if (remaining.Remove(name))
                ordered.Add(name);
        }

        return [.. remaining.OrderBy(n => n, StringComparer.Ordinal), .. ordered];
    }

    public static void Move<T>(List<T> order, int from, int to)
    {
        if (from == to || from < 0 || from >= order.Count || to < 0 || to >= order.Count)
            return;

        var moved = order[from];
        order.RemoveAt(from);
        order.Insert(to, moved);
    }
}
