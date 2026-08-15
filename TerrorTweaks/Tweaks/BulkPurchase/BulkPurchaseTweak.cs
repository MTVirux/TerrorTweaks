using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using TerrorTweaks.Framework;
using TerrorTweaks.Util;

namespace TerrorTweaks.Tweaks.BulkPurchase;

internal readonly record struct ShopItemSnapshot(
    uint ItemId,
    string Name,
    int Index,
    int UnitPrice,
    int StackSize,
    int Owned);

public sealed class BulkPurchaseTweak : Tweak
{
    private const string ShopAddonName = "Shop";

    // How many completed transactions may pass without the owned count moving before the
    // job is treated as stuck (full inventory, unaffordable, or a rejected purchase).
    private const int StallLimit = 3;

    private readonly BulkPurchaseWindow _window;

    private ShopItemSnapshot _job;
    private int _total;
    private int _remaining;
    private long _nextPurchaseAt;
    private int _lastOwned;
    private int _stalls;
    private bool _running;

    public BulkPurchaseTweak()
    {
        _window = new BulkPurchaseWindow(this);
    }

    public override string Name => "Bulk Purchase";

    public override string Description =>
        "Adds a \"Bulk Purchase\" entry to gil vendor item context menus that buys any amount " +
        "you enter, repeating the purchase as many times as the vendor's per-transaction cap " +
        "needs - 99 at a time for stackable items, one at a time for those that do not stack.";

    public override void Enable()
    {
        base.Enable();
        Services.ContextMenu.OnMenuOpened += OnMenuOpened;
        Services.PluginInterface.UiBuilder.Draw += _window.Draw;
    }

    public override void Disable()
    {
        if (_running)
            Finish("the tweak was turned off");

        Services.PluginInterface.UiBuilder.Draw -= _window.Draw;
        Services.ContextMenu.OnMenuOpened -= OnMenuOpened;
        _window.Close();
        base.Disable();
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType != ContextMenuType.Default || args.AddonName != ShopAddonName)
            return;

        if (ResolveHoveredItem() is not { } item)
            return;

        args.AddMenuItem(MenuPrefix.Item("Bulk Purchase", _ => _window.Open(item)));
    }

    internal bool IsRunning => _running;
    internal int Total => _total;
    internal int Remaining => _remaining;

    internal static unsafe long Gil
    {
        get
        {
            var manager = InventoryManager.Instance();
            return manager is null ? 0 : manager->GetGil();
        }
    }

    internal static unsafe int FreeSlots
    {
        get
        {
            var manager = InventoryManager.Instance();
            return manager is null ? 0 : (int)manager->GetEmptySlotsInBag();
        }
    }

    // Re-reads the item straight from the shop each frame so the window shows a live owned
    // count and closes itself when the shop goes away. Null means "no longer purchasable".
    internal unsafe ShopItemSnapshot? Refresh(uint itemId)
    {
        var handler = Handler();
        if (handler is null)
            return null;

        var index = FindIndex(handler, itemId);
        return index < 0 ? null : Snapshot(handler, index, itemId);
    }

    internal void Start(ShopItemSnapshot item, int amount)
    {
        if (_running || amount <= 0)
            return;

        _job = item;
        _total = amount;
        _remaining = amount;
        _lastOwned = item.Owned;
        _stalls = 0;
        _nextPurchaseAt = 0;
        _running = true;

        LogIndexDiagnostics(item);
        Services.Framework.Update += OnFrameworkUpdate;
        Services.Chat.Print($"Bulk Purchase: buying {amount:N0} {item.Name}.");
    }

    internal void Stop()
    {
        if (_running)
            Finish("you stopped it");
    }

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        if (!_running)
            return;

        var handler = Handler();
        if (handler is null)
        {
            Finish(_remaining <= 0 ? null : "the shop closed");
            return;
        }

        if (handler->WaitingForTransactionToFinish || Environment.TickCount64 < _nextPurchaseAt)
            return;

        // Summarise only once the final transaction has settled, so the game's own purchase
        // message for it prints before ours.
        if (_remaining <= 0)
        {
            Finish(null);
            return;
        }

        var index = FindIndex(handler, _job.ItemId);
        if (index < 0)
        {
            Finish("the item is no longer listed");
            return;
        }

        var owned = handler->Items[index].NumOwned;
        if (_remaining < _total)
        {
            if (owned > _lastOwned)
            {
                _stalls = 0;
            }
            else if (++_stalls >= StallLimit)
            {
                Finish("purchases stopped going through (inventory full?)");
                return;
            }
        }

        _lastOwned = owned;

        var batch = BulkPurchasePlan.NextBatch(_remaining, handler->Items[index].StackSize);
        if (Gil < (long)batch * handler->Items[index].PriceBuy)
        {
            Finish("you ran out of gil");
            return;
        }

        handler->BuyItemIndex = index;
        handler->ExecuteBuy(batch);

        _remaining -= batch;
        _nextPurchaseAt = Environment.TickCount64 + Math.Max(0, Plugin.Config.BulkPurchase.DelayMs);
    }

    private void Finish(string? reason)
    {
        var bought = _total - Math.Max(0, _remaining);
        _running = false;
        Services.Framework.Update -= OnFrameworkUpdate;

        Services.Chat.Print(reason is null
            ? $"Bulk Purchase: bought {bought:N0} {_job.Name}."
            : $"Bulk Purchase: stopped after {bought:N0} {_job.Name} - {reason}.");
    }

    private static unsafe ShopItemSnapshot? ResolveHoveredItem()
    {
        var handler = Handler();
        if (handler is null)
        {
            Services.Log.Debug("Bulk Purchase: shop event handler unavailable.");
            return null;
        }

        var agent = AgentItemDetail.Instance();
        var itemId = agent is null ? 0 : ItemIdNormalizer.ToBaseItemId(agent->ItemId);
        if (itemId == 0)
        {
            Services.Log.Debug("Bulk Purchase: AgentItemDetail did not report a hovered item.");
            return null;
        }

        var index = FindIndex(handler, itemId);
        if (index < 0)
        {
            Services.Log.Debug($"Bulk Purchase: item {itemId} is not among the shop's {handler->ItemsCount} listings.");
            return null;
        }

        return Snapshot(handler, index, itemId);
    }

    private static unsafe ShopItemSnapshot Snapshot(ShopEventHandler* handler, int index, uint itemId)
    {
        ref var item = ref handler->Items[index];
        return new ShopItemSnapshot(
            itemId,
            ItemNames.Lookup(itemId),
            index,
            item.PriceBuy,
            item.StackSize,
            item.NumOwned);
    }

    private static unsafe int FindIndex(ShopEventHandler* handler, uint itemId)
    {
        var items = handler->Items;
        var count = Math.Min(handler->ItemsCount, items.Length);
        for (var i = 0; i < count; i++)
        {
            if (ItemIdNormalizer.ToBaseItemId(items[i].ItemId) == itemId)
                return i;
        }

        return -1;
    }

    // Only the Items index is used for BuyItemIndex; the other two are logged so a live run
    // can confirm that assumption if a purchase ever lands on the wrong item.
    private static unsafe void LogIndexDiagnostics(ShopItemSnapshot item)
    {
        var handler = Handler();
        if (handler is null)
            return;

        var visibleItems = handler->VisibleItems;
        var count = Math.Min(handler->VisibleItemsCount, visibleItems.Length);
        var visibleIndex = -1;
        for (var i = 0; i < count; i++)
        {
            if (visibleItems[i] == item.Index)
            {
                visibleIndex = i;
                break;
            }
        }

        var agent = AgentShop.Instance();
        Services.Log.Debug(
            $"Bulk Purchase: {item.Name} at items index {item.Index}, visible index {visibleIndex}, " +
            $"agent selected index {(agent is null ? -1 : agent->SelectedItemIndex)}.");
    }

    private static unsafe ShopEventHandler* Handler()
    {
        var agent = AgentShop.Instance();
        if (agent is null || !agent->IsAgentActive())
            return null;

        var proxy = ShopEventHandler.AgentProxy.Instance();
        return proxy is null ? null : proxy->Handler;
    }

    public override void DrawConfig()
    {
        var cfg = Plugin.Config.BulkPurchase;
        var delay = cfg.DelayMs;

        ImGui.SetNextItemWidth(160);
        if (ImGui.SliderInt("Delay between purchases (ms)##BulkPurchase", ref delay, 100, 3000))
        {
            cfg.DelayMs = delay;
            Plugin.Config.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Waited on top of the game confirming each purchase.");
    }
}
