using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace TerrorTweaks.Framework;

// Dalamud insists on exactly one boxed letter in front of every plugin menu item and always
// renders it as "<letter> ", so a tight "TT" is only possible by drawing both letters in the name
// and stripping the one Dalamud re-added out of the built ContextMenu addon. StripColor is a
// sentinel that marks our own prefix; a real boxed T is used with it so a missed strip degrades
// into a stray T rather than a garbage glyph.
internal static unsafe class MenuPrefix
{
    private const ushort PrefixColor = 539;
    private const ushort StripColor = 65534;

    internal static MenuItem Item(string name, Action<IMenuItemClickedArgs> onClicked) => new()
    {
        Name = new SeStringBuilder()
            .AddUiForeground($"{SeIconChar.BoxedLetterT.ToIconString()}{SeIconChar.BoxedLetterT.ToIconString()} ", PrefixColor)
            .AddText(name)
            .Build(),
        Prefix = SeIconChar.BoxedLetterT,
        PrefixColor = StripColor,
        OnClicked = onClicked,
    };

    internal static void Register() =>
        Services.AddonLifecycle.RegisterListener(AddonEvent.PreRequestedUpdate, "ContextMenu", OnContextMenuUpdate);

    internal static void Unregister() =>
        Services.AddonLifecycle.UnregisterListener(AddonEvent.PreRequestedUpdate, "ContextMenu", OnContextMenuUpdate);

    private static void OnContextMenuUpdate(AddonEvent type, AddonArgs args)
    {
        var addon = (AddonContextMenu*)args.Addon.Address;
        if (addon is null)
            return;

        // Where the entry names start moves with Dalamud's header layout, so sweep every value;
        // StripColor is ours alone, so anything that does not match is left untouched.
        for (var i = 0; i < addon->AtkValuesCount; i++)
        {
            var entry = addon->AtkValues[i];
            if (entry.Type is not (AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString))
                continue;

            if (entry.String.Value is null)
                continue;

            var text = MemoryHelper.ReadSeStringNullTerminated((nint)entry.String.Value);
            if (StripOwnPrefix(text))
                MemoryHelper.WriteSeString((nint)entry.String.Value, text);
        }
    }

    private static bool StripOwnPrefix(SeString text)
    {
        for (var i = 1; i < text.Payloads.Count; i++)
        {
            if (text.Payloads[i - 1] is not UIForegroundPayload { ColorKey: StripColor })
                continue;

            if (text.Payloads[i] is not TextPayload { Text: [(char)SeIconChar.BoxedLetterT, ..] })
                continue;

            text.Payloads.RemoveAt(i);
            // The rewrite reuses the game's buffer, so keep it null terminated at the new length.
            text.Payloads.Add(new TextPayload("\0"));
            return true;
        }

        return false;
    }
}
