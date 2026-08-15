using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using TerrorTweaks.Util;

namespace TerrorTweaks.Tweaks.ListingHelper;

internal enum BagSide
{
    Player,
    Retainer,
}

internal readonly record struct SourceSlot(InventoryType Container, int Slot, int Quantity);

// A retainer sells out of two different places: "Sell items" puts up what is in the player's
// bags, while the retainer's own inventory puts up what it is already holding. Only one of the
// two windows is on screen at a time, and that is the side a copy has to come out of - listing
// from the other one is not something the game offers from there.
internal static unsafe class DuplicateSource
{
    private static readonly InventoryType[] PlayerBags =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    private static readonly InventoryType[] RetainerBags =
    [
        InventoryType.RetainerPage1,
        InventoryType.RetainerPage2,
        InventoryType.RetainerPage3,
        InventoryType.RetainerPage4,
        InventoryType.RetainerPage5,
        InventoryType.RetainerPage6,
        InventoryType.RetainerPage7,
    ];

    private static readonly string[] RetainerAddons = ["InventoryRetainerLarge", "InventoryRetainer"];

    private static readonly string[] PlayerAddons = ["InventoryLarge", "InventoryExpansion", "Inventory"];

    internal static BagSide Side() => VisibleAddon(RetainerAddons) is null ? BagSide.Player : BagSide.Retainer;

    // Which side a container belongs to, and null for anything that is not a bag at all - both
    // windows can be up at once, so the container is a surer answer than which one is on screen.
    internal static BagSide? SideOf(InventoryType type)
    {
        if (Array.IndexOf(PlayerBags, type) >= 0)
            return BagSide.Player;

        return Array.IndexOf(RetainerBags, type) >= 0 ? BagSide.Retainer : null;
    }

    internal static string Describe(BagSide side) =>
        side == BagSide.Retainer ? "the retainer's inventory" : "your inventory";

    // The menu is anchored to the window the item is drawn in; the sell list is the fallback
    // because it is the one window that is always up while any of this runs.
    internal static uint OwnerAddonId(BagSide side)
    {
        var addon = VisibleAddon(side == BagSide.Retainer ? RetainerAddons : PlayerAddons);
        if (addon is null)
            addon = LoadedAddon("RetainerSellList");

        return addon is null ? 0u : addon->Id;
    }

    internal static List<SourceSlot> Find(MarketTarget target, BagSide side)
    {
        var found = new List<SourceSlot>();
        var manager = InventoryManager.Instance();
        if (manager is null)
            return found;

        foreach (var type in side == BagSide.Retainer ? RetainerBags : PlayerBags)
        {
            var container = manager->GetInventoryContainer(type);
            if (container is null || !container->IsLoaded)
                continue;

            for (var i = 0; i < container->Size; i++)
            {
                var item = container->GetInventorySlot(i);
                if (Matches(item, target))
                    found.Add(new SourceSlot(type, i, item->Quantity));
            }
        }

        return found;
    }

    // A copy is put up out of a single slot, so the first one holding a whole stack is the one
    // the next copy comes from.
    internal static SourceSlot? FirstHolding(MarketTarget target, BagSide side, int quantity)
    {
        foreach (var slot in Find(target, side))
        {
            if (slot.Quantity >= quantity)
                return slot;
        }

        return null;
    }

    // Quality is never ignored here: a duplicate that swapped an HQ listing for an NQ stack
    // would put up an offer nobody asked for.
    private static bool Matches(InventoryItem* item, MarketTarget target) =>
        item is not null
        && item->ItemId != 0
        && ItemIdNormalizer.ToBaseItemId(item->ItemId) == target.ItemId
        && item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) == target.HighQuality;

    private static AtkUnitBase* VisibleAddon(string[] names)
    {
        foreach (var name in names)
        {
            var addon = LoadedAddon(name);
            if (addon is not null && addon->IsVisible)
                return addon;
        }

        return null;
    }

    private static AtkUnitBase* LoadedAddon(string name) =>
        (AtkUnitBase*)(nint)Services.GameGui.GetAddonByName(name);
}
