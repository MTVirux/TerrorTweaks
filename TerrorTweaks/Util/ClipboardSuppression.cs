using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrorTweaks.Util;

internal readonly record struct MenuEntry(string Text, bool FromPlugin);

internal static class ClipboardSuppression
{
    // Stops the learned list growing without bound if some plugin names its entry after the
    // item it was opened on.
    public const int MaxLearned = 200;

    // Dalamud renders every plugin entry behind a boxed-letter glyph from the private use
    // area. Strip it so learned strings read the way the player sees them and the glyph never
    // has to be typed into a match.
    public static string Clean(string raw)
    {
        var start = 0;
        while (start < raw.Length && (IsPluginGlyph(raw[start]) || char.IsWhiteSpace(raw[start])))
            start++;

        return raw[start..].Trim();
    }

    public static List<MenuEntry> Read(IEnumerable<string> rawEntries, string? ownEntry)
    {
        var entries = new List<MenuEntry>();
        var ownFound = ownEntry is null;

        foreach (var raw in rawEntries)
        {
            var text = Clean(raw);
            if (text.Length == 0)
                continue;

            var fromPlugin = raw.Length > 0 && IsPluginGlyph(raw[0]);

            // Our own entry is in the menu on every open we injected on. Drop exactly one copy
            // so it neither gets learned nor suppresses us, while a second plugin wording its
            // entry the same way still counts.
            if (!ownFound && fromPlugin && text == ownEntry)
            {
                ownFound = true;
                continue;
            }

            entries.Add(new MenuEntry(text, fromPlugin));
        }

        return entries;
    }

    public static bool ShouldSuppress(IEnumerable<MenuEntry> entries, IReadOnlyCollection<string> matches)
        => matches.Count > 0 && entries.Any(entry => matches.Any(match =>
            match.Length > 0 && entry.Text.Contains(match, StringComparison.OrdinalIgnoreCase)));

    public static bool Learn(List<string> learned, IEnumerable<MenuEntry> entries)
    {
        var changed = false;

        foreach (var entry in entries)
        {
            if (!entry.FromPlugin || learned.Count >= MaxLearned)
                continue;

            if (learned.Contains(entry.Text, StringComparer.OrdinalIgnoreCase))
                continue;

            learned.Add(entry.Text);
            changed = true;
        }

        if (changed)
            learned.Sort(StringComparer.OrdinalIgnoreCase);

        return changed;
    }

    private static bool IsPluginGlyph(char c) => c is >= (char)0xE000 and <= (char)0xF8FF;
}
