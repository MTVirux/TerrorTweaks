using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Inventory;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using TerrorTweaks.Framework;
using TerrorTweaks.Util;

namespace TerrorTweaks.Tweaks;

internal readonly record struct MarketTarget(uint ItemId, bool HighQuality);

public sealed class RetainerPriceUpdateTweak : Tweak
{
    private const string SellListAddonName = "RetainerSellList";
    private const string SellAddonName = "RetainerSell";
    private const string ContextMenuAddonName = "ContextMenu";

    // The game builds "Adjust Price" as the first entry of a market slot's context menu, and
    // Dalamud appends plugin entries after the native ones, so this index stays put.
    private const int AdjustPriceEntry = 0;

    // Every step waits on local UI only, so anything slower than this means the flow broke.
    private const int StepTimeoutMs = 3000;

    private enum Step
    {
        OpenMenu,
        PickAdjustPrice,
        EnterPrice,
        Settle,
    }

    private readonly List<int> _slots = [];

    private MarketTarget _target;
    private string _itemName = string.Empty;
    private int _price;
    private int _slotIndex;
    private int _updated;
    private Step _step;
    private long _stepDeadline;
    private long _resumeAt;
    private bool _running;

    public override string Name => "Retainer Price Update";

    public override string Description =>
        "Adds an \"Update entries of this item\" entry to the retainer sell list that reprices " +
        "every listing of that item to the gil amount on your clipboard.";

    public override void Enable()
    {
        base.Enable();
        Services.ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    public override void Disable()
    {
        if (_running)
            Finish("the tweak was turned off");

        Services.ContextMenu.OnMenuOpened -= OnMenuOpened;
        base.Disable();
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (ResolveTarget(args) is not { } target)
            return;

        args.AddMenuItem(MenuPrefix.Item("Update entries of this item", _ => Start(target)));
    }

    private static MarketTarget? ResolveTarget(IMenuOpenedArgs args)
    {
        if (args.Target is MenuTargetInventory inventory)
        {
            return inventory.TargetItem is { ContainerType: GameInventoryType.RetainerMarket } item
                ? new MarketTarget(ItemIdNormalizer.ToBaseItemId(item.ItemId), item.IsHq)
                : null;
        }

        if (args.MenuType != ContextMenuType.Default || args.AddonName != SellListAddonName)
            return null;

        var itemId = ReadItemDetailAgentItemId();
        return itemId == 0
            ? null
            : new MarketTarget(ItemIdNormalizer.ToBaseItemId(itemId), ItemIdNormalizer.IsHighQuality(itemId));
    }

    private void Start(MarketTarget target)
    {
        if (_running)
        {
            Services.Chat.Print("Retainer Price: the last update is still running.");
            return;
        }

        var clipboard = ImGui.GetClipboardText() ?? string.Empty;
        if (!ClipboardPrice.TryParse(clipboard, out var price))
        {
            Services.Chat.PrintError($"Retainer Price: the clipboard does not hold a price ({QuoteClipboard(clipboard)}).");
            return;
        }

        var slots = FindSlots(target);
        if (slots.Count == 0)
        {
            Services.Chat.PrintError("Retainer Price: this retainer has no listing of that item.");
            return;
        }

        _slots.Clear();
        _slots.AddRange(slots);
        _target = target;
        _itemName = ItemName(target);
        _price = price;
        _slotIndex = 0;
        _updated = 0;
        _running = true;
        BeginStep(Step.OpenMenu);
        _resumeAt = 0;

        LogListings();
        Services.Framework.Update += OnFrameworkUpdate;
        Services.Chat.Print($"Retainer Price: setting {Listings(_slots.Count)} of {_itemName} to {price:N0} gil.");
    }

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        if (!_running || Environment.TickCount64 < _resumeAt)
            return;

        if (LoadedAddon(SellListAddonName) is null)
        {
            Finish("the sell list closed");
            return;
        }

        if (Environment.TickCount64 > _stepDeadline)
        {
            Finish($"the game stopped responding while it was {StepDescription(_step)}");
            return;
        }

        switch (_step)
        {
            case Step.OpenMenu:
                OpenMenu();
                break;
            case Step.PickAdjustPrice:
                PickAdjustPrice();
                break;
            case Step.EnterPrice:
                EnterPrice();
                break;
            case Step.Settle:
                Settle();
                break;
        }
    }

    private unsafe void OpenMenu()
    {
        var slot = _slots[_slotIndex];
        if (!SlotMatches(slot))
        {
            Finish("the listings moved around");
            return;
        }

        // The menu the user clicked ours from can still be on screen; opening ours while it is
        // would leave the next step firing at somebody else's item.
        if (VisibleAddon(ContextMenuAddonName) is not null)
            return;

        var sellList = LoadedAddon(SellListAddonName);
        var context = AgentInventoryContext.Instance();
        if (sellList is null || context is null)
        {
            Finish("the retainer window went away");
            return;
        }

        context->OpenForItemSlot(InventoryType.RetainerMarket, slot, 0, sellList->Id);
        BeginStep(Step.PickAdjustPrice);
    }

    private unsafe void PickAdjustPrice()
    {
        var menu = VisibleAddon(ContextMenuAddonName);
        if (menu is null || !menu->IsReady)
            return;

        LogMenuEntries(menu);

        var values = stackalloc AtkValue[5];
        values[0] = new AtkValue { Type = AtkValueType.Int, Int = 0 };
        values[1] = new AtkValue { Type = AtkValueType.Int, Int = AdjustPriceEntry };
        values[2] = new AtkValue { Type = AtkValueType.UInt, UInt = 0 };
        values[3] = new AtkValue { Type = AtkValueType.UInt, UInt = 0 };
        values[4] = new AtkValue { Type = AtkValueType.Null };
        menu->FireCallback(5, values, true);

        BeginStep(Step.EnterPrice);
    }

    private unsafe void EnterPrice()
    {
        var sell = (AddonRetainerSell*)(nint)Services.GameGui.GetAddonByName(SellAddonName);
        if (sell is null || !sell->AtkUnitBase.IsVisible || !sell->AtkUnitBase.IsReady || sell->AskingPrice is null)
            return;

        // Writing the input keeps the window honest even if the callback below is what the
        // game actually reads.
        sell->AskingPrice->SetValue(_price);

        var values = stackalloc AtkValue[2];
        values[0] = new AtkValue { Type = AtkValueType.Int, Int = 0 };
        values[1] = new AtkValue { Type = AtkValueType.Int, Int = _price };
        sell->AtkUnitBase.FireCallback(2, values, true);

        _updated++;
        BeginStep(Step.Settle);
    }

    private unsafe void Settle()
    {
        if (VisibleAddon(SellAddonName) is not null)
            return;

        _slotIndex++;
        if (_slotIndex >= _slots.Count)
        {
            Finish(null);
            return;
        }

        BeginStep(Step.OpenMenu);
        _resumeAt = Environment.TickCount64 + Math.Max(0, Plugin.Config.RetainerPrice.DelayMs);
    }

    private void BeginStep(Step step)
    {
        _step = step;
        _stepDeadline = Environment.TickCount64 + StepTimeoutMs;
    }

    private void Finish(string? reason)
    {
        _running = false;
        Services.Framework.Update -= OnFrameworkUpdate;

        Services.Chat.Print(reason is null
            ? $"Retainer Price: updated {Listings(_updated)} of {_itemName} to {_price:N0} gil."
            : $"Retainer Price: stopped after {Listings(_updated)} of {_itemName} - {reason}.");
    }

    private static unsafe List<int> FindSlots(MarketTarget target)
    {
        var slots = new List<int>();
        var container = MarketContainer();
        if (container is null)
            return slots;

        for (var i = 0; i < container->Size; i++)
        {
            if (Matches(container->GetInventorySlot(i), target))
                slots.Add(i);
        }

        return slots;
    }

    private unsafe bool SlotMatches(int slot)
    {
        var container = MarketContainer();
        return container is not null && slot < container->Size && Matches(container->GetInventorySlot(slot), _target);
    }

    private static unsafe bool Matches(InventoryItem* item, MarketTarget target) =>
        item is not null
        && item->ItemId != 0
        && ItemIdNormalizer.ToBaseItemId(item->ItemId) == target.ItemId
        && item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) == target.HighQuality;

    private static unsafe InventoryContainer* MarketContainer()
    {
        var manager = InventoryManager.Instance();
        if (manager is null)
            return null;

        var container = manager->GetInventoryContainer(InventoryType.RetainerMarket);
        return container is not null && container->IsLoaded ? container : null;
    }

    private static unsafe AtkUnitBase* LoadedAddon(string name) =>
        (AtkUnitBase*)(nint)Services.GameGui.GetAddonByName(name);

    // The sell list stays loaded but can be flagged invisible behind the dialogs this drives,
    // so only the dialogs themselves are checked for visibility.
    private static unsafe AtkUnitBase* VisibleAddon(string name)
    {
        var addon = LoadedAddon(name);
        return addon is not null && addon->IsVisible ? addon : null;
    }

    private static unsafe uint ReadItemDetailAgentItemId()
    {
        var agent = AgentItemDetail.Instance();
        return agent is null ? 0 : agent->ItemId;
    }

    private static string ItemName(MarketTarget target)
    {
        var name = ItemNames.Lookup(target.ItemId);
        return target.HighQuality ? $"{name} (HQ)" : name;
    }

    private static string Listings(int count) => count == 1 ? "1 listing" : $"{count} listings";

    private static string QuoteClipboard(string clipboard)
    {
        var text = clipboard.Trim().ReplaceLineEndings(" ");
        return text.Length == 0 ? "it is empty" : $"\"{(text.Length > 24 ? text[..24] + "..." : text)}\"";
    }

    private static string StepDescription(Step step) => step switch
    {
        Step.OpenMenu => "opening the listing's menu",
        Step.PickAdjustPrice => "picking Adjust Price",
        Step.EnterPrice => "waiting for the price window",
        _ => "closing the price window",
    };

    private unsafe void LogListings()
    {
        var manager = InventoryManager.Instance();
        foreach (var slot in _slots)
        {
            var price = manager is null ? 0 : manager->GetRetainerMarketPrice((short)slot);
            Services.Log.Debug($"Retainer Price: slot {slot} of {_itemName} currently at {price} gil.");
        }
    }

    // The adjust-price entry is assumed to be first; logging what the menu actually held makes
    // a wrong pick obvious in the log instead of silent.
    private static unsafe void LogMenuEntries(AtkUnitBase* menu)
    {
        var entries = new List<string>();
        for (var i = 0; i < menu->AtkValuesCount; i++)
        {
            var value = menu->AtkValues[i];
            if (value.Type is not (AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString))
                continue;

            if (value.String.Value is null)
                continue;

            entries.Add(MemoryHelper.ReadSeStringNullTerminated((nint)value.String.Value).TextValue);
        }

        Services.Log.Debug($"Retainer Price: context menu held [{string.Join(" | ", entries)}]");
    }

    public override void DrawConfig()
    {
        var cfg = Plugin.Config.RetainerPrice;
        var delay = cfg.DelayMs;

        ImGui.SetNextItemWidth(160);
        if (ImGui.SliderInt("Delay between listings (ms)##RetainerPrice", ref delay, 100, 3000))
        {
            cfg.DelayMs = delay;
            Plugin.Config.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Waited after each listing before the next one is repriced.");
    }
}
