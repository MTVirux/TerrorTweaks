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

namespace TerrorTweaks.Tweaks.RetainerPriceUpdate;

internal readonly record struct MarketTarget(uint ItemId, bool HighQuality);

internal readonly record struct AddonBounds(float X, float Y, float Width, float Height);

internal readonly record struct PanelRow(
    MarketTarget Target,
    string Name,
    int Listings,
    int Quantity,
    int CurrentPrice,
    bool MixedPrices);

public sealed class RetainerPriceUpdateTweak : Tweak
{
    private const string SellListAddonName = "RetainerSellList";
    private const string SellAddonName = "RetainerSell";
    private const string SearchResultAddonName = "ItemSearchResult";
    private const string ContextMenuAddonName = "ContextMenu";

    // The game builds "Adjust Price" as the first entry of a market slot's context menu, and
    // Dalamud appends plugin entries after the native ones, so this index stays put.
    private const int AdjustPriceEntry = 0;

    // The sell window's Compare Prices button answers on this callback value. Confirming a
    // price rides the same callback with a different one, so a wrong value here reprices a
    // live listing instead of opening the board.
    private const int ComparePricesCallback = 4;

    // Atk's near-universal cancel value, used to close the windows this drives back down.
    private const int CancelCallback = -1;

    // Every UI step waits on local UI only, so anything slower than this means the flow broke.
    private const int StepTimeoutMs = 3000;

    // A price lookup crosses the server and arrives over several packets, and a throttled
    // request never answers at all, so this bounds the wait rather than measuring it.
    private const int ListingsTimeoutMs = 10000;

    // How long the packets have to stop coming before the answer counts as complete.
    private const int PageQuietMs = 1500;

    private enum RunMode
    {
        Reprice,
        Lookup,
    }

    private enum Step
    {
        OpenMenu,
        PickAdjustPrice,
        EnterPrice,
        Settle,
        ComparePrices,
        AwaitListings,
        CloseSearch,
        CancelSell,
    }

    private readonly record struct Job(MarketTarget Target, int Slot, int Price);

    private readonly Dictionary<MarketTarget, List<MarketListing>> _cache = [];
    private readonly HashSet<ulong> _ownRetainers = [];
    private readonly MarketLookup _lookup = new();
    private readonly List<Job> _jobs = [];
    private readonly RetainerPricePanel _panel;

    private RunMode _mode;
    private string _runLabel = string.Empty;
    private int _jobIndex;
    private int _updated;
    private Step _step;
    private long _stepDeadline;
    private long _resumeAt;
    private bool _running;
    private bool _ignoreQuality;

    // A window takes a frame or two to go away, and firing cancel at it again in the meantime
    // would land on whatever has opened behind it.
    private bool _cancelFired;

    public RetainerPriceUpdateTweak()
    {
        _panel = new RetainerPricePanel(this);
    }

    public override string Name => "Retainer Price Update";

    public override string Description =>
        "Adds an \"Update entries of this item\" entry to the retainer sell list that reprices " +
        "every listing of that item to the gil amount on your clipboard, plus a panel for " +
        "undercutting the market board price of everything the retainer has listed.";

    public override void Enable()
    {
        base.Enable();
        Services.ContextMenu.OnMenuOpened += OnMenuOpened;
        Services.PluginInterface.UiBuilder.Draw += _panel.Draw;
    }

    public override void Disable()
    {
        if (_running)
            Finish("the tweak was turned off");

        Services.PluginInterface.UiBuilder.Draw -= _panel.Draw;
        Services.ContextMenu.OnMenuOpened -= OnMenuOpened;
        _lookup.Stop();
        _cache.Clear();
        _panel.Reset();
        base.Disable();
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (ResolveTarget(args) is not { } target)
            return;

        args.AddMenuItem(MenuPrefix.Item("Update entries of this item", _ => StartFromClipboard(target)));
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

    private void StartFromClipboard(MarketTarget target)
    {
        if (!CanStart())
            return;

        var clipboard = ImGui.GetClipboardText() ?? string.Empty;
        if (!ClipboardPrice.TryParse(clipboard, out var price))
        {
            Services.Chat.PrintError($"Retainer Price: the clipboard does not hold a price ({QuoteClipboard(clipboard)}).");
            return;
        }

        var ignoreQuality = Plugin.Config.RetainerPrice.IgnoreQuality;
        var slots = FindSlots(target, ignoreQuality);
        if (slots.Count == 0)
        {
            Services.Chat.PrintError("Retainer Price: this retainer has no listing of that item.");
            return;
        }

        var jobs = new List<Job>();
        foreach (var slot in slots)
            jobs.Add(new Job(target, slot, price));

        var name = ItemName(target.ItemId, target.HighQuality && !ignoreQuality);
        Begin(RunMode.Reprice, jobs, ignoreQuality, $"{Listings(jobs.Count)} of {name} to {price:N0} gil");
    }

    internal void ApplyAll(IReadOnlyDictionary<MarketTarget, int> prices)
    {
        if (!CanStart())
            return;

        var ignoreQuality = Plugin.Config.RetainerPrice.IgnoreQuality;
        var jobs = new List<Job>();
        var items = 0;

        foreach (var row in Rows())
        {
            if (!prices.TryGetValue(row.Target, out var price) || price < UndercutCalculator.MinPrice)
                continue;

            // Nothing to gain from walking a listing that already sits at the wanted price, and
            // every skipped one is a few seconds off the run.
            if (!row.MixedPrices && row.CurrentPrice == price)
                continue;

            var slots = FindSlots(row.Target, ignoreQuality);
            if (slots.Count == 0)
                continue;

            items++;
            foreach (var slot in slots)
                jobs.Add(new Job(row.Target, slot, price));
        }

        if (jobs.Count == 0)
        {
            Services.Chat.Print("Retainer Price: nothing to apply - every listing is already at its price.");
            return;
        }

        Begin(RunMode.Reprice, jobs, ignoreQuality, $"{Listings(jobs.Count)} across {items} item(s)");
    }

    internal void RequestPrices(IReadOnlyList<MarketTarget> targets)
    {
        if (!CanStart())
            return;

        var ignoreQuality = Plugin.Config.RetainerPrice.IgnoreQuality;
        var jobs = new List<Job>();

        RefreshOwnRetainers();

        foreach (var target in targets)
        {
            // Already looked up this session, so the price is recomputed from the stored
            // listings instead of asking the server a second time.
            if (Price(target) is { } cached)
            {
                _panel.SetPrice(target, cached);
                continue;
            }

            var slots = FindSlots(target, ignoreQuality);
            if (slots.Count > 0)
                jobs.Add(new Job(target, slots[0], 0));
        }

        if (jobs.Count == 0)
            return;

        Begin(RunMode.Lookup, jobs, ignoreQuality, $"{jobs.Count} item(s)");
    }

    private bool CanStart()
    {
        if (!_running)
            return true;

        Services.Chat.Print("Retainer Price: the last run is still going.");
        return false;
    }

    private void Begin(RunMode mode, List<Job> jobs, bool ignoreQuality, string label)
    {
        _jobs.Clear();
        _jobs.AddRange(jobs);
        _mode = mode;
        _runLabel = label;
        _ignoreQuality = ignoreQuality;
        _jobIndex = 0;
        _updated = 0;
        _cancelFired = false;
        _running = true;
        _resumeAt = 0;
        RefreshOwnRetainers();
        BeginStep(Step.OpenMenu);

        Services.Framework.Update += OnFrameworkUpdate;
        Services.Chat.Print(mode == RunMode.Reprice
            ? $"Retainer Price: setting {label}."
            : $"Retainer Price: checking market prices for {label}.");
    }

    internal void Stop()
    {
        if (_running)
            Finish("you stopped it");
    }

    internal bool IsRunning => _running;

    internal string Status => _running
        ? $"{StepDescription(_step)} ({_jobIndex + 1}/{_jobs.Count})"
        : string.Empty;

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
            StepTimedOut();
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
            case Step.ComparePrices:
                ComparePrices();
                break;
            case Step.AwaitListings:
                AwaitListings();
                break;
            case Step.CloseSearch:
                CloseSearch();
                break;
            case Step.CancelSell:
                CancelSell();
                break;
        }
    }

    // A lookup that never answers is expected - an unlisted item and a throttled request look
    // exactly alike - so it records an empty result and carries on instead of killing the run.
    private unsafe void StepTimedOut()
    {
        switch (_step)
        {
            case Step.AwaitListings:
                FinishLookup(true);
                BeginStep(Step.CloseSearch);
                break;
            case Step.CloseSearch:
                ForceClose(SearchResultAddonName);
                _cancelFired = false;
                BeginStep(Step.CancelSell);
                break;
            case Step.CancelSell:
                ForceClose(SellAddonName);
                NextJob();
                break;
            default:
                Finish($"the game stopped responding while it was {StepDescription(_step)}");
                break;
        }
    }

    private Job Current => _jobs[_jobIndex];

    private unsafe void OpenMenu()
    {
        var job = Current;
        if (!SlotMatches(job.Slot, job.Target))
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

        context->OpenForItemSlot(InventoryType.RetainerMarket, job.Slot, 0, sellList->Id);
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

        BeginStep(_mode == RunMode.Reprice ? Step.EnterPrice : Step.ComparePrices);
    }

    private unsafe void EnterPrice()
    {
        var sell = SellAddon();
        if (sell is null || sell->AskingPrice is null)
            return;

        // Writing the input keeps the window honest even if the callback below is what the
        // game actually reads.
        sell->AskingPrice->SetValue(Current.Price);

        var values = stackalloc AtkValue[2];
        values[0] = new AtkValue { Type = AtkValueType.Int, Int = 0 };
        values[1] = new AtkValue { Type = AtkValueType.Int, Int = Current.Price };
        sell->AtkUnitBase.FireCallback(2, values, true);

        _updated++;
        BeginStep(Step.Settle);
    }

    private unsafe void Settle()
    {
        if (VisibleAddon(SellAddonName) is not null)
            return;

        NextJob();
    }

    // A lookup opens the same Adjust Price window a reprice does, purely because that is the
    // only place Compare Prices exists, and must leave it by cancelling - anything that
    // confirms would commit whatever price the window happens to be holding.
    private unsafe void ComparePrices()
    {
        var sell = SellAddon();
        if (sell is null)
            return;

        _lookup.Begin(Current.Target.ItemId);

        var values = stackalloc AtkValue[1];
        values[0] = new AtkValue { Type = AtkValueType.Int, Int = ComparePricesCallback };
        sell->AtkUnitBase.FireCallback(1, values, true);

        BeginStep(Step.AwaitListings, ListingsTimeoutMs);
    }

    private void AwaitListings()
    {
        // A last page that happens to be exactly full is indistinguishable from one with more
        // behind it, so a lull after at least one page is what marks the answer as finished.
        var quiet = _lookup.Listings.Count > 0
            && Environment.TickCount64 - _lookup.LastPageAt > PageQuietMs;

        if (!_lookup.Complete && !quiet)
            return;

        FinishLookup(false);
        BeginStep(Step.CloseSearch);
    }

    private void FinishLookup(bool timedOut)
    {
        var target = Current.Target;

        // Nothing arrived at all, and a throttled query looks exactly like an item nobody is
        // selling, so this one is reported but never cached - Update has to be able to retry.
        if (timedOut && _lookup.Listings.Count == 0)
        {
            Services.Log.Debug($"Retainer Price: no listings arrived for item {target.ItemId}.");
            _panel.SetPrice(target, new UndercutResult(UndercutOutcome.NoListings, 0));
            return;
        }

        RecordListings(target, _lookup.Listings);
    }

    private unsafe void CloseSearch()
    {
        var search = VisibleAddon(SearchResultAddonName);
        if (search is null)
        {
            _cancelFired = false;
            BeginStep(Step.CancelSell);
            return;
        }

        if (_cancelFired)
            return;

        FireCancel(search);
        _cancelFired = true;
    }

    private unsafe void CancelSell()
    {
        var sell = VisibleAddon(SellAddonName);
        if (sell is null)
        {
            _cancelFired = false;
            NextJob();
            return;
        }

        if (_cancelFired)
            return;

        FireCancel(sell);
        _cancelFired = true;
    }

    private void NextJob()
    {
        _lookup.Stop();
        _cancelFired = false;
        _jobIndex++;

        if (_jobIndex >= _jobs.Count)
        {
            Finish(null);
            return;
        }

        var cfg = Plugin.Config.RetainerPrice;
        var delay = _mode == RunMode.Reprice ? cfg.DelayMs : cfg.LookupDelayMs;
        _resumeAt = Environment.TickCount64 + Math.Max(0, delay);
        BeginStep(Step.OpenMenu);
    }

    private void RecordListings(MarketTarget target, IReadOnlyList<MarketListing> listings)
    {
        _cache[target] = [.. listings];
        if (Price(target) is { } result)
            _panel.SetPrice(target, result);
    }

    internal UndercutResult? Price(MarketTarget target)
    {
        if (!_cache.TryGetValue(target, out var listings))
            return null;

        var cfg = Plugin.Config.RetainerPrice;

        // A run's rows were keyed under the setting it started with, so it keeps pricing under
        // that one - a mid-run toggle would otherwise drop an NQ price into an HQ box.
        var ignoreQuality = _running ? _ignoreQuality : cfg.IgnoreQuality;

        return UndercutCalculator.Resolve(
            listings,
            target.HighQuality,
            ignoreQuality,
            cfg.UndercutGil,
            _ownRetainers);
    }

    private void BeginStep(Step step) => BeginStep(step, StepTimeoutMs);

    private void BeginStep(Step step, int timeoutMs)
    {
        _step = step;

        // The clock starts when the step is allowed to run, not now - a pacing delay longer
        // than the timeout would otherwise expire the step before it ever got a frame.
        _stepDeadline = Math.Max(Environment.TickCount64, _resumeAt) + timeoutMs;
    }

    private void Finish(string? reason)
    {
        _running = false;
        _lookup.Stop();
        _cancelFired = false;
        Services.Framework.Update -= OnFrameworkUpdate;

        if (_mode == RunMode.Lookup)
        {
            Services.Chat.Print(reason is null
                ? $"Retainer Price: checked market prices for {_runLabel}."
                : $"Retainer Price: stopped checking prices - {reason}.");
            return;
        }

        Services.Chat.Print(reason is null
            ? $"Retainer Price: updated {_runLabel}."
            : $"Retainer Price: stopped after {Listings(_updated)} - {reason}.");
    }

    internal static unsafe List<PanelRow> Rows()
    {
        var rows = new List<PanelRow>();
        var container = MarketContainer();
        var manager = InventoryManager.Instance();
        if (container is null || manager is null)
            return rows;

        // With quality ignored a listing's HQ flag stops being part of its identity, so both
        // tiers of an item collapse into the one row that will reprice all of them.
        var ignoreQuality = Plugin.Config.RetainerPrice.IgnoreQuality;
        var index = new Dictionary<MarketTarget, int>();

        for (var i = 0; i < container->Size; i++)
        {
            var item = container->GetInventorySlot(i);
            if (item is null || item->ItemId == 0)
                continue;

            var highQuality = !ignoreQuality && item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality);
            var target = new MarketTarget(ItemIdNormalizer.ToBaseItemId(item->ItemId), highQuality);
            var price = (int)Math.Clamp(manager->GetRetainerMarketPrice((short)i), 0, int.MaxValue);

            if (index.TryGetValue(target, out var at))
            {
                var row = rows[at];
                rows[at] = row with
                {
                    Listings = row.Listings + 1,
                    Quantity = row.Quantity + item->Quantity,
                    MixedPrices = row.MixedPrices || row.CurrentPrice != price,
                };
                continue;
            }

            index[target] = rows.Count;
            rows.Add(new PanelRow(target, ItemName(target.ItemId, highQuality), 1, item->Quantity, price, false));
        }

        return rows;
    }

    internal static unsafe bool SellListOpen() => LoadedAddon(SellListAddonName) is not null;

    // Deliberately not gated on visibility: the sell list is flagged invisible behind the
    // dialogs a run drives, and the panel should stay put rather than jump around mid-run.
    internal static unsafe AddonBounds? SellListBounds()
    {
        var addon = LoadedAddon(SellListAddonName);
        if (addon is null || addon->RootNode is null)
            return null;

        var scale = addon->Scale;
        return new AddonBounds(addon->X, addon->Y, addon->RootNode->Width * scale, addon->RootNode->Height * scale);
    }

    internal static unsafe ulong ActiveRetainerId()
    {
        var manager = RetainerManager.Instance();
        if (manager is null)
            return 0;

        var active = manager->GetActiveRetainer();
        return active is null ? 0 : active->RetainerId;
    }

    private unsafe void RefreshOwnRetainers()
    {
        _ownRetainers.Clear();

        var manager = RetainerManager.Instance();
        if (manager is null)
            return;

        // Only the retainer being edited is hidden from the results - the character's other
        // retainers list like anyone else, and cutting under those walks our own prices down.
        foreach (ref var retainer in manager->Retainers)
        {
            if (retainer.RetainerId != 0)
                _ownRetainers.Add(retainer.RetainerId);
        }
    }

    private static unsafe List<int> FindSlots(MarketTarget target, bool ignoreQuality)
    {
        var slots = new List<int>();
        var container = MarketContainer();
        if (container is null)
            return slots;

        for (var i = 0; i < container->Size; i++)
        {
            if (Matches(container->GetInventorySlot(i), target, ignoreQuality))
                slots.Add(i);
        }

        return slots;
    }

    private unsafe bool SlotMatches(int slot, MarketTarget target)
    {
        var container = MarketContainer();
        return container is not null
            && slot < container->Size
            && Matches(container->GetInventorySlot(slot), target, _ignoreQuality);
    }

    private static unsafe bool Matches(InventoryItem* item, MarketTarget target, bool ignoreQuality) =>
        item is not null
        && item->ItemId != 0
        && ItemIdNormalizer.ToBaseItemId(item->ItemId) == target.ItemId
        && (ignoreQuality || item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) == target.HighQuality);

    private static unsafe InventoryContainer* MarketContainer()
    {
        var manager = InventoryManager.Instance();
        if (manager is null)
            return null;

        var container = manager->GetInventoryContainer(InventoryType.RetainerMarket);
        return container is not null && container->IsLoaded ? container : null;
    }

    private static unsafe AddonRetainerSell* SellAddon()
    {
        var sell = (AddonRetainerSell*)(nint)Services.GameGui.GetAddonByName(SellAddonName);
        return sell is not null && sell->AtkUnitBase.IsVisible && sell->AtkUnitBase.IsReady ? sell : null;
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

    private static unsafe void FireCancel(AtkUnitBase* addon)
    {
        var values = stackalloc AtkValue[1];
        values[0] = new AtkValue { Type = AtkValueType.Int, Int = CancelCallback };
        addon->FireCallback(1, values, true);
    }

    // Last resort when the cancel callback did not take: a window left open blocks every job
    // queued behind it.
    private static unsafe void ForceClose(string name)
    {
        var addon = VisibleAddon(name);
        if (addon is null)
            return;

        Services.Log.Warning($"Retainer Price: {name} ignored its cancel callback, closing it directly.");
        addon->Close(true);
    }

    private static unsafe uint ReadItemDetailAgentItemId()
    {
        var agent = AgentItemDetail.Instance();
        return agent is null ? 0 : agent->ItemId;
    }

    internal static string ItemName(uint itemId, bool highQuality)
    {
        var name = ItemNames.Lookup(itemId);
        return highQuality ? $"{name} (HQ)" : name;
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
        Step.ComparePrices => "opening the market board",
        Step.AwaitListings => "waiting for market prices",
        Step.CloseSearch => "closing the market board",
        _ => "closing the price window",
    };

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

        var showPanel = cfg.ShowPanel;
        if (ImGui.Checkbox("Show panel at the retainer sell list##RetainerPrice", ref showPanel))
        {
            cfg.ShowPanel = showPanel;
            Plugin.Config.Save();
        }

        var dock = cfg.DockToSellList;
        if (ImGui.Checkbox("Dock it to the sell list##RetainerPrice", ref dock))
        {
            cfg.DockToSellList = dock;
            Plugin.Config.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Pins the panel to the right of the retainer window and matches its height.");

        var ignoreQuality = cfg.IgnoreQuality;
        if (ImGui.Checkbox("Ignore NQ/HQ##RetainerPrice", ref ignoreQuality))
        {
            cfg.IgnoreQuality = ignoreQuality;
            Plugin.Config.Save();

            // Rows are keyed on quality, so the two tiers merge or split under this and any
            // price already typed would land against the wrong one.
            _panel.Reset();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Treat both qualities of an item as one thing to price, instead of keeping them apart.");

        var undercut = cfg.UndercutGil;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputInt("Undercut by (gil)##RetainerPrice", ref undercut))
        {
            cfg.UndercutGil = Math.Clamp(undercut, 0, UndercutCalculator.MaxPrice);
            Plugin.Config.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Taken off the lowest listing found. Your own retainers are never undercut.");

        var delay = cfg.DelayMs;
        ImGui.SetNextItemWidth(160);
        if (ImGui.SliderInt("Delay between listings (ms)##RetainerPrice", ref delay, 100, 3000))
        {
            cfg.DelayMs = delay;
            Plugin.Config.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Waited after each listing before the next one is repriced.");

        var lookupDelay = cfg.LookupDelayMs;
        ImGui.SetNextItemWidth(160);
        if (ImGui.SliderInt("Delay between price checks (ms)##RetainerPrice", ref lookupDelay, 1000, 10000))
        {
            cfg.LookupDelayMs = lookupDelay;
            Plugin.Config.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Market board queries are rate limited by the server. Lower this at your own risk.");
    }
}
