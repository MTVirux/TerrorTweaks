using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.ContextMenu;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using TerrorTweaks.Framework;
using TerrorTweaks.Util;

namespace TerrorTweaks.Tweaks;

public sealed class ClipboardTweak : Tweak
{
    public override string Name => "Clipboard";

    public override string Description =>
        "Adds a \"Clipboard\" entry to item context menus that copies the item's name.";

    // Default-type menus only expose the hovered item through AgentItemDetail; restrict to
    // surfaces where that agent is reliably populated. Extend this list to add coverage.
    private static readonly string[] DefaultAddonAllowlist =
    [
        "ItemSearch", "ChatLog", "RecipeNote", "GatheringNote",
        "Shop", "InclusionShop", "ShopExchangeCurrency", "ShopExchangeItem",
    ];

    public override void Enable()
    {
        base.Enable();
        Services.ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    public override void Disable()
    {
        Services.ContextMenu.OnMenuOpened -= OnMenuOpened;
        base.Disable();
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        var itemId = ResolveItemId(args);
        if (itemId == 0)
            return;

        var name = ItemNames.Lookup(itemId);
        if (string.IsNullOrEmpty(name))
            return;

        args.AddMenuItem(new MenuItem
        {
            Name = "Clipboard",
            PrefixChar = 'T',
            OnClicked = _ => Copy(name),
        });
    }

    private static uint ResolveItemId(IMenuOpenedArgs args)
    {
        if (args.Target is MenuTargetInventory inv)
            return inv.TargetItem?.ItemId ?? 0;

        if (args.MenuType == ContextMenuType.Default
            && args.AddonName is { } addon
            && DefaultAddonAllowlist.Contains(addon))
        {
            return ReadItemDetailAgentItemId();
        }

        return 0;
    }

    private static unsafe uint ReadItemDetailAgentItemId()
    {
        var agent = AgentItemDetail.Instance();
        return agent is null ? 0 : agent->ItemId;
    }

    private static void Copy(string name)
    {
        ImGui.SetClipboardText(name);
        Services.Log.Debug($"Copied item name to clipboard: {name}");
    }
}
