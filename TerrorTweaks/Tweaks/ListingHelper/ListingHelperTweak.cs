using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Inventory;
using Dalamud.Game.Text;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using TerrorTweaks.Framework;
using TerrorTweaks.Util;

namespace TerrorTweaks.Tweaks.ListingHelper;

internal readonly record struct MarketTarget(uint ItemId, bool HighQuality);

internal readonly record struct AddonBounds(float X, float Y, float Width, float Height);

// One market listing as it stands right now - what a duplicate of it has to look like.
internal readonly record struct ListingSource(MarketTarget Target, int Price, int Quantity);

// One stack sitting in a bag, waiting to be put up.
internal readonly record struct StockSource(MarketTarget Target, int Quantity, BagSide Side);

internal readonly record struct PanelRow(
    MarketTarget Target,
    string Name,
    int Listings,
    int Quantity,
    int CurrentPrice,
    bool MixedPrices);

public sealed class ListingHelperTweak : Tweak
{
    private const string SellListAddonName = "RetainerSellList";
    private const string SellAddonName = "RetainerSell";
    private const string SearchResultAddonName = "ItemSearchResult";
    private const string ContextMenuAddonName = "ContextMenu";
    private const string InputNumericAddonName = "InputNumeric";

    // The game builds "Adjust Price" as the first entry of a market slot's context menu, and
    // Dalamud appends plugin entries after the native ones, so this index stays put. An index
    // rather than a label, so it needs nothing from the client's language.
    private const int AdjustPriceEntry = 0;

    // Addon sheet row for the inventory menu's "Put Up for Sale" entry, looked up rather than
    // written out because the menu is drawn in the client's language.
    private const uint PutUpForSaleRow = 99;

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

    // The sell list has no mapped struct, so its running order comes out of its AtkValues: one
    // block per row from this index, whose first Int is the market slot drawn on that row.
    private const int OrderBaseIndex = 15;
    private const int OrderStride = 13;

    // The market container is twenty slots, so the window can never draw more rows than that.
    private const int MaxListings = 20;

    // How long a stored answer stands for. The market moves under it, and an undercut worked out
    // from a page this old is a guess rather than a price.
    private const int CacheLifetimeMs = 180_000;

    // The game's own HQ symbol. Dalamud merges the game symbol font into its default one, so
    // this renders in the panel as well as in chat.
    private const char HighQualityGlyph = (char)SeIconChar.HighQuality;

    private enum RunMode
    {
        Reprice,
        Lookup,
        Remove,
        Duplicate,
        ListUndercut,
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
        SettleRemove,
        PickPutUpForSale,
        EnterListing,
        SettleDuplicate,
        SettleMerge,
    }

    private readonly record struct Job(MarketTarget Target, int Slot, int Price)
    {
        // A duplicate acts on a bag slot rather than a market one, so it carries which side of
        // the sale it draws from and how big the stack it is copying is.
        public BagSide Side { get; init; }

        public int Quantity { get; init; }
    }

    private readonly record struct CachedListings(List<MarketListing> Listings, long RecordedAt);

    // Everything the fallback needs, taken at the moment the request goes out rather than when
    // it comes back.
    private readonly record struct UniversalisRequest(
        UniversalisSource Source,
        bool IgnoreQuality,
        int UndercutGil,
        string World,
        string DataCentre,
        ulong RetainerId,
        bool BoardAnswered);

    private readonly Dictionary<MarketTarget, CachedListings> _cache = [];
    private readonly HashSet<ulong> _ownRetainers = [];
    private readonly MarketLookup _lookup = new();
    private readonly List<Job> _jobs = [];
    private readonly ListingHelperPanel _panel;

    // A Universalis answer lands long after the step that asked for it, so turning the tweak off
    // has to be able to drop one that is still on its way.
    private CancellationTokenSource _web = new();

    private RunMode _mode;
    private BagSide _returnTo;
    private string _runLabel = string.Empty;
    private int _jobIndex;
    private int _updated;
    private int _matchesBefore;
    private int _listedBefore;

    // The slot a merge is pouring into, and what it held before - the only way to tell the move
    // landed, since the game reports nothing when it does not.
    private SourceSlot _mergeTarget;
    private int _mergeBefore;
    private Step _step;
    private long _stepDeadline;
    private long _resumeAt;
    private bool _running;
    private bool _ignoreQuality;

    // A window takes a frame or two to go away, and firing cancel at it again in the meantime
    // would land on whatever has opened behind it.
    private bool _cancelFired;

    // A broken sheet row is worth saying once, not on every menu the run opens.
    private bool _warnedMissingLabel;

    public ListingHelperTweak()
    {
        _panel = new ListingHelperPanel(this);
    }

    public override string Name => "Listing Helper";

    public override string Description =>
        "Adds a panel to the retainer sell list for undercutting the market board price of " +
        "everything the retainer has listed, plus \"Duplicate Listing\" and \"Recursive " +
        "Duplicate\" entries that put up more copies of a listing from the same stock, and a " +
        "\"List Undercut\" entry that puts a stack up at the price the market board says.";

    public override void Enable()
    {
        base.Enable();
        _warnedMissingLabel = false;
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
        _web.Cancel();
        _web.Dispose();
        _web = new CancellationTokenSource();
        _cache.Clear();
        _panel.Reset();
        base.Disable();
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (ResolveListing(args) is { } listing)
        {
            args.AddMenuItem(MenuPrefix.Item("Duplicate Listing", _ => Duplicate(listing, 1)));
            args.AddMenuItem(MenuPrefix.Item("Recursive Duplicate", _ => Duplicate(listing, DuplicatePlan.MarketSlots)));
            return;
        }

        if (ResolveStock(args) is { } stock)
            args.AddMenuItem(MenuPrefix.Item("List Undercut", _ => ListUndercut(stock)));
    }

    // The bag side of the same menu: an item sitting in a bag with the sell list up is one the
    // game itself would offer to put on the market, so that is where this is offered too.
    private static unsafe StockSource? ResolveStock(IMenuOpenedArgs args)
    {
        if (!SellListOpen() || args.Target is not MenuTargetInventory inventory)
            return null;

        if (inventory.TargetItem is not { } item)
            return null;

        if (DuplicateSource.SideOf((InventoryType)item.ContainerType) is not { } side)
            return null;

        var read = ReadBagItem((InventoryType)item.ContainerType, (int)item.InventorySlot, side);
        return read is { } stock && Marketable(stock.Target.ItemId) ? stock : null;
    }

    private static unsafe StockSource? ReadBagItem(InventoryType type, int slot, BagSide side)
    {
        var manager = InventoryManager.Instance();
        if (manager is null)
            return null;

        var container = manager->GetInventoryContainer(type);
        if (container is null || !container->IsLoaded || slot < 0 || slot >= container->Size)
            return null;

        var item = container->GetInventorySlot(slot);
        if (item is null || item->ItemId == 0)
            return null;

        var target = new MarketTarget(
            ItemIdNormalizer.ToBaseItemId(item->ItemId),
            item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality));

        return new StockSource(target, item->Quantity, side);
    }

    // Anything the market board will not carry has no price to undercut, and the game offers no
    // way to list it either. Unmarketable items sit in search category zero.
    private static bool Marketable(uint itemId) =>
        Services.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>().TryGetRow(itemId, out var row)
        && row.ItemSearchCategory.RowId != 0;

    private void ListUndercut(StockSource stock)
    {
        if (!CanStart())
            return;

        if (OccupiedMarketSlots() >= DuplicatePlan.MarketSlots)
        {
            Services.Chat.PrintError("Listing Helper: this retainer has no market slot left to list into.");
            return;
        }

        if (DuplicateSource.FirstHolding(stock.Target, stock.Side, stock.Quantity) is null)
        {
            Services.Chat.PrintError(
                $"Listing Helper: {DuplicateSource.Describe(stock.Side)} no longer holds that stack.");
            return;
        }

        // The price is not known yet - it comes off the market board partway through the run.
        var jobs = new List<Job>
        {
            new(stock.Target, 0, 0) { Side = stock.Side, Quantity = stock.Quantity },
        };

        var name = ItemName(stock.Target.ItemId, stock.Target.HighQuality);
        Begin(
            RunMode.ListUndercut,
            jobs,
            Plugin.Config.ListingHelper.IgnoreQuality,
            $"{stock.Quantity}x {name}");
    }

    private static unsafe ListingSource? ResolveListing(IMenuOpenedArgs args)
    {
        if (args.Target is MenuTargetInventory inventory)
        {
            return inventory.TargetItem is { ContainerType: GameInventoryType.RetainerMarket } item
                ? ReadListing((int)item.InventorySlot)
                : null;
        }

        if (args.MenuType != ContextMenuType.Default || args.AddonName != SellListAddonName)
            return null;

        var itemId = ReadItemDetailAgentItemId();
        if (itemId == 0)
            return null;

        // This path only reports an item, not which of its listings was clicked, so the first
        // one of that exact quality stands in for it.
        var target = new MarketTarget(ItemIdNormalizer.ToBaseItemId(itemId), ItemIdNormalizer.IsHighQuality(itemId));
        return ReadListing(FirstSlot(target, false));
    }

    private static unsafe ListingSource? ReadListing(int slot)
    {
        var container = MarketContainer();
        var manager = InventoryManager.Instance();
        if (container is null || manager is null || slot < 0 || slot >= container->Size)
            return null;

        var item = container->GetInventorySlot(slot);
        if (item is null || item->ItemId == 0)
            return null;

        var target = new MarketTarget(
            ItemIdNormalizer.ToBaseItemId(item->ItemId),
            item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality));

        var price = (int)Math.Clamp(manager->GetRetainerMarketPrice((short)slot), 0, int.MaxValue);
        return new ListingSource(target, price, item->Quantity);
    }

    private void Duplicate(ListingSource listing, int wanted)
    {
        if (!CanStart())
            return;

        var side = DuplicateSource.Side();
        var sources = DuplicateSource.Find(listing.Target, side);

        var stacks = new List<int>(sources.Count);
        foreach (var source in sources)
            stacks.Add(source.Quantity);

        var plan = DuplicatePlan.Build(
            stacks,
            listing.Quantity,
            OccupiedMarketSlots(),
            wanted,
            Plugin.Config.ListingHelper.MergeSplitStacks);

        if (plan.Block == DuplicateBlock.MarketFull)
        {
            Services.Chat.PrintError("Listing Helper: this retainer has no market slot left to list into.");
            return;
        }

        var name = ItemName(listing.Target.ItemId, listing.Target.HighQuality);
        if (!plan.CanStart)
        {
            Services.Chat.PrintError(
                $"Listing Helper: {DuplicateSource.Describe(side)} does not hold another {listing.Quantity} of {name}.");
            return;
        }

        var jobs = new List<Job>();
        for (var copy = 0; copy < plan.Copies; copy++)
            jobs.Add(new Job(listing.Target, 0, listing.Price) { Side = side, Quantity = listing.Quantity });

        // Carried through only so the panel keeps pricing the way it was; a duplicate itself
        // always matches quality exactly, whatever this says.
        Begin(
            RunMode.Duplicate,
            jobs,
            Plugin.Config.ListingHelper.IgnoreQuality,
            $"{Listings(plan.Copies)} of {listing.Quantity}x {name} at {listing.Price:N0} gil");
    }

    internal void ApplyAll(IReadOnlyDictionary<MarketTarget, int> prices) => Apply(prices, null);

    internal void Apply(MarketTarget target, int price) =>
        Apply(new Dictionary<MarketTarget, int> { [target] = price }, target);

    private void Apply(IReadOnlyDictionary<MarketTarget, int> prices, MarketTarget? only)
    {
        if (!CanStart())
            return;

        var ignoreQuality = Plugin.Config.ListingHelper.IgnoreQuality;
        var jobs = new List<Job>();
        var items = 0;

        foreach (var row in Rows())
        {
            if (only is { } wanted && row.Target != wanted)
                continue;

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
            Services.Chat.Print(only is null
                ? "Listing Helper: nothing to apply - every listing is already at its price."
                : "Listing Helper: nothing to apply - that listing is already at its price.");
            return;
        }

        Begin(RunMode.Reprice, jobs, ignoreQuality, $"{Listings(jobs.Count)} across {items} item(s)");
    }

    internal void RemoveListings(MarketTarget target, BagSide destination)
    {
        if (!CanStart())
            return;

        var ignoreQuality = Plugin.Config.ListingHelper.IgnoreQuality;
        var slots = FindSlots(target, ignoreQuality);
        if (slots.Count == 0)
        {
            Services.Chat.PrintError("Listing Helper: this retainer has no listing of that item.");
            return;
        }

        var jobs = new List<Job>();
        foreach (var slot in slots)
            jobs.Add(new Job(target, slot, 0));

        _returnTo = destination;

        var name = ItemName(target.ItemId, target.HighQuality && !ignoreQuality);
        Begin(RunMode.Remove, jobs, ignoreQuality, $"{Listings(jobs.Count)} of {name}");
    }

    internal void RequestPrices(IReadOnlyList<MarketTarget> targets)
    {
        if (!CanStart())
            return;

        var ignoreQuality = Plugin.Config.ListingHelper.IgnoreQuality;
        var jobs = new List<Job>();
        var names = new List<string>();

        RefreshOwnRetainers();

        foreach (var target in targets)
        {
            // Update means ask the server again - the market moves under a stored answer, and a
            // button that quietly reused one would look broken.
            _cache.Remove(target);

            var slots = FindSlots(target, ignoreQuality);
            if (slots.Count == 0)
                continue;

            jobs.Add(new Job(target, slots[0], 0));
            names.Add(ItemName(target.ItemId, target.HighQuality && !ignoreQuality));
        }

        if (jobs.Count == 0)
            return;

        Begin(RunMode.Lookup, jobs, ignoreQuality, string.Join(", ", names));
    }

    private bool CanStart()
    {
        if (!_running)
            return true;

        Services.Chat.Print("Listing Helper: the last run is still going.");
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
        Services.Chat.Print(mode switch
        {
            RunMode.Reprice => $"Listing Helper: setting {label}.",
            RunMode.Remove =>
                $"Listing Helper: taking {label} off the market and into {DuplicateSource.Describe(_returnTo)}.",
            RunMode.Duplicate => $"Listing Helper: putting up {label}.",
            RunMode.ListUndercut => $"Listing Helper: checking the market before listing {label}.",
            _ => $"Listing Helper: checking market prices for {label}.",
        });
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
            case Step.SettleRemove:
                SettleRemove();
                break;
            case Step.PickPutUpForSale:
                PickPutUpForSale();
                break;
            case Step.EnterListing:
                EnterListing();
                break;
            case Step.SettleDuplicate:
                SettleDuplicate();
                break;
            case Step.SettleMerge:
                SettleMerge();
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
                BeginStep(AfterSearch());
                break;
            case Step.CancelSell:
                ForceClose(SellAddonName);
                NextJob();
                break;
            case Step.ComparePrices:
            case Step.EnterListing:
            case Step.SettleDuplicate:
                CloseStrandedSell();
                Finish($"the game stopped responding while it was {StepDescription(_step)}");
                break;
            case Step.SettleMerge:
                // A move the game answered with its quantity dialog would sit there unanswered,
                // and a modal left open blocks everything behind it.
                ForceClose(InputNumericAddonName);
                Finish("the split stacks would not merge");
                break;
            default:
                Finish($"the game stopped responding while it was {StepDescription(_step)}");
                break;
        }
    }

    private Job Current => _jobs[_jobIndex];

    private unsafe void OpenMenu()
    {
        if (_mode is RunMode.Duplicate or RunMode.ListUndercut)
        {
            OpenSourceMenu();
            return;
        }

        var job = Current;

        // Pulling a listing renumbers what sits behind it, so removal re-finds its target every
        // time rather than trusting an index taken before the run started.
        var slot = _mode == RunMode.Remove ? FirstSlot(job.Target, _ignoreQuality) : job.Slot;
        if (slot < 0)
        {
            NextJob();
            return;
        }

        if (!SlotMatches(slot, job.Target))
        {
            Finish("the listings moved around");
            return;
        }

        if (_mode == RunMode.Remove)
        {
            ReturnListing(slot);
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

        _matchesBefore = FindSlots(job.Target, _ignoreQuality).Count;

        context->OpenForItemSlot(InventoryType.RetainerMarket, slot, 0, sellList->Id);
        BeginStep(Step.PickAdjustPrice);
    }

    private unsafe void PickAdjustPrice()
    {
        var menu = VisibleAddon(ContextMenuAddonName);
        if (menu is null || !menu->IsReady)
            return;

        LogMenuEntries(menu);
        FireMenuEntry(menu, AdjustPriceEntry);

        BeginStep(_mode == RunMode.Reprice ? Step.EnterPrice : Step.ComparePrices);
    }

    // Neither destination can be reached from the menu that opens for a market slot: it offers the
    // player's side only, and the entry that hands a stack back to the retainer belongs to the menu
    // the sell list builds for itself on a right click, which nothing here can make it build. So a
    // removal moves the stack outright, the way the game's own entries do.
    private unsafe void ReturnListing(int slot)
    {
        var manager = InventoryManager.Instance();
        var container = MarketContainer();
        if (manager is null || container is null)
        {
            Finish("the retainer window went away");
            return;
        }

        var item = container->GetInventorySlot(slot);
        if (item is null || item->ItemId == 0)
        {
            NextJob();
            return;
        }

        _matchesBefore = FindSlots(Current.Target, _ignoreQuality).Count;

        var quantity = (uint)item->Quantity;
        var moved = _returnTo == BagSide.Retainer
            ? manager->MoveFromRetainerMarketToRetainerInventory(InventoryType.RetainerMarket, (ushort)slot, quantity)
            : manager->MoveFromRetainerMarketToPlayerInventory(InventoryType.RetainerMarket, (ushort)slot, quantity);

        Services.Log.Debug($"Listing Helper: moved slot {slot} to {_returnTo}, got {moved}.");
        BeginStep(Step.SettleRemove);
    }

    private unsafe void SettleRemove()
    {
        // The listing is only really gone once the server drops it out of the container, which
        // is also the only confirmation that the move did what it was supposed to.
        if (FindSlots(Current.Target, _ignoreQuality).Count >= _matchesBefore)
            return;

        _updated++;
        NextJob();
    }

    private unsafe void OpenSourceMenu()
    {
        var job = Current;

        // Putting a stack up empties the bag slot it came out of and shuffles what follows, so
        // the source is found again for every copy rather than trusted from before the run.
        if (DuplicateSource.FirstHolding(job.Target, job.Side, job.Quantity) is not { } source)
        {
            if (!StartMerge(job))
                Finish($"{DuplicateSource.Describe(job.Side)} ran out of that item");

            return;
        }

        // The menu the user clicked ours from can still be on screen; opening ours while it is
        // would leave the next step firing at somebody else's item.
        if (VisibleAddon(ContextMenuAddonName) is not null)
            return;

        var context = AgentInventoryContext.Instance();
        if (context is null)
        {
            Finish("the retainer window went away");
            return;
        }

        _matchesBefore = OccupiedMarketSlots();
        _listedBefore = MarketQuantity(job.Target);

        context->OpenForItemSlot(source.Container, source.Slot, 0, DuplicateSource.OwnerAddonId(job.Side));
        BeginStep(Step.PickPutUpForSale);
    }

    // A copy comes out of one bag slot, so stock split across several is poured together first -
    // three partial stacks that add up would be three refusals otherwise. One pair per pass: the
    // slots are read again from the game between moves rather than planned out in advance.
    private bool StartMerge(Job job)
    {
        if (!Plugin.Config.ListingHelper.MergeSplitStacks)
            return false;

        var slots = DuplicateSource.Find(job.Target, job.Side);
        var stacks = new List<int>(slots.Count);
        foreach (var slot in slots)
            stacks.Add(slot.Quantity);

        var cap = DuplicateSource.MaxStackSize(job.Target.ItemId);
        if (StackMergePlan.Next(stacks, job.Quantity, cap) is not { } merge)
            return false;

        _mergeTarget = slots[merge.To];
        _mergeBefore = _mergeTarget.Quantity;
        DuplicateSource.Merge(slots[merge.From], _mergeTarget);

        BeginStep(Step.SettleMerge);
        return true;
    }

    private void SettleMerge()
    {
        // The items have only really moved once the destination holds more than it did, which is
        // also the only confirmation the move was accepted.
        if (DuplicateSource.QuantityAt(_mergeTarget) <= _mergeBefore)
            return;

        // Back round the same step: one merge may not be enough, and the next pass picks the next
        // pair off freshly read slots.
        BeginStep(Step.OpenMenu);
    }

    // A bag item's menu holds a different set depending on where the player is standing, so the
    // sale entry is found by its label. Firing a guessed index would discard or use the stack.
    private unsafe void PickPutUpForSale()
    {
        var menu = VisibleAddon(ContextMenuAddonName);
        if (menu is null || !menu->IsReady)
            return;

        var entries = MenuEntries(menu);
        Services.Log.Debug($"Listing Helper: context menu held [{string.Join(" | ", entries)}]");

        var label = PutUpForSaleLabel();
        var entry = entries.FindIndex(e => e.Contains(label, StringComparison.OrdinalIgnoreCase));
        if (entry < 0)
        {
            Finish($"nothing on the menu offered to put it up for sale - it held [{string.Join(" | ", entries)}]");
            return;
        }

        FireMenuEntry(menu, entry);

        // A duplicate already knows its price, and so does an undercut whose item was looked up
        // recently enough - only the rest has to go out to the market board first.
        var priced = _mode == RunMode.Duplicate || Cached(Current.Target);
        BeginStep(priced ? Step.EnterListing : Step.ComparePrices);
    }

    private unsafe void EnterListing()
    {
        var sell = SellAddon();
        if (sell is null || sell->AskingPrice is null || sell->Quantity is null)
            return;

        if (_mode == RunMode.ListUndercut && !TakeUndercutPrice())
            return;

        var job = Current;
        sell->Quantity->SetValue(job.Quantity);
        sell->AskingPrice->SetValue(job.Price);

        // Same window and same confirm button as a reprice, so it answers on the same callback;
        // the quantity is read off the input above. Logged because a fresh listing is the one
        // place that pairing has not been proven, and a wrong one puts up a wrong offer.
        Services.Log.Debug($"Listing Helper: listing {job.Quantity}x item {job.Target.ItemId} at {job.Price} gil.");

        var values = stackalloc AtkValue[2];
        values[0] = new AtkValue { Type = AtkValueType.Int, Int = 0 };
        values[1] = new AtkValue { Type = AtkValueType.Int, Int = job.Price };
        sell->AtkUnitBase.FireCallback(2, values, true);

        BeginStep(Step.SettleDuplicate);
    }

    // An item nobody is selling has no price to cut under, and neither has one whose lookup never
    // answered - a guessed number would go up as a real offer, so the run stops instead.
    private bool TakeUndercutPrice()
    {
        var job = Current;
        if (Price(job.Target) is not { Outcome: not UndercutOutcome.NoListings } result)
        {
            CloseStrandedSell();
            Finish("nothing came back off the market board to undercut");
            return false;
        }

        _jobs[_jobIndex] = job with { Price = result.Price };
        _runLabel = $"{_runLabel} at {result.Price:N0} gil";
        return true;
    }

    private unsafe void SettleDuplicate()
    {
        if (VisibleAddon(SellAddonName) is not null)
            return;

        // The copy only exists once the server puts it in the market container, which is also
        // the only confirmation that the callback above did what it was supposed to.
        if (OccupiedMarketSlots() <= _matchesBefore)
            return;

        // The sell window defaults to the whole stack, so a quantity that did not take would
        // list far more than was asked for. Checked here rather than trusted, because a
        // recursive run would otherwise repeat the same mistake nineteen more times.
        var listed = MarketQuantity(Current.Target) - _listedBefore;
        if (listed != Current.Quantity)
        {
            _updated++;
            Finish($"a copy went up as {listed} rather than {Current.Quantity}");
            return;
        }

        _updated++;
        NextJob();
    }

    // The entry's own label out of the Addon sheet, which Dalamud serves in whatever language the
    // client is running, so a German or Japanese menu matches as well as an English one. Row 99 is
    // the inventory menu's sale entry - the same row AutoRetainer reads for it.
    private string PutUpForSaleLabel()
    {
        var sheet = Services.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Addon>();
        if (sheet.TryGetRow(PutUpForSaleRow, out var row) && row.Text.ExtractText() is { Length: > 0 } label)
            return label;

        // A game update that moved the row leaves every non-English client with no way to find the
        // entry, which is a plugin bug rather than anything the player did. Said once: the sheet
        // will not answer differently later in the session.
        if (!_warnedMissingLabel)
        {
            _warnedMissingLabel = true;
            Services.Chat.PrintError(
                $"Listing Helper: the game has no text for Addon row {PutUpForSaleRow}, so the sale " +
                "entry can only be matched in English. Please let @mtvirux know on Discord.");
        }

        // An empty needle would match the first entry on the menu, whatever it happens to be.
        return "Put Up for Sale";
    }

    private static unsafe void FireMenuEntry(AtkUnitBase* menu, int entry)
    {
        var values = stackalloc AtkValue[5];
        values[0] = new AtkValue { Type = AtkValueType.Int, Int = 0 };
        values[1] = new AtkValue { Type = AtkValueType.Int, Int = entry };
        values[2] = new AtkValue { Type = AtkValueType.UInt, UInt = 0 };
        values[3] = new AtkValue { Type = AtkValueType.UInt, UInt = 0 };
        values[4] = new AtkValue { Type = AtkValueType.Null };
        menu->FireCallback(5, values, true);
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

        if (!_lookup.Complete && !quiet && !NothingListed())
            return;

        FinishLookup(false);
        BeginStep(Step.CloseSearch);
    }

    // An item nobody is selling sends no offerings packet at all, so waiting for one is waiting
    // for something that is never coming. The history landing says the server answered, and the
    // search the client is holding says what it answered with.
    private unsafe bool NothingListed()
    {
        if (!_lookup.Answered || _lookup.Listings.Count > 0)
            return false;

        var search = InfoProxyItemSearch.Instance();
        if (search is null
            || search->WaitingForListings
            || search->ListingCount != 0
            || ItemIdNormalizer.ToBaseItemId(search->SearchItemId) != Current.Target.ItemId)
        {
            return false;
        }

        Services.Log.Debug($"Listing Helper: item {Current.Target.ItemId} came back with nothing listed.");
        return true;
    }

    private void FinishLookup(bool timedOut)
    {
        var target = Current.Target;

        // Nothing arrived at all, and a throttled query looks exactly like an item nobody is
        // selling, so this one is reported but never cached - Update has to be able to retry.
        if (timedOut && _lookup.Listings.Count == 0)
        {
            Services.Log.Debug($"Listing Helper: no listings arrived for item {target.ItemId}.");
            _panel.SetPrice(target, new UndercutResult(UndercutOutcome.NoListings, 0));
            RequestUniversalisPrice(target, false);
            return;
        }

        RecordListings(target, _lookup.Listings);
    }

    // A lookup leaves the sell window by cancelling it. An undercut has to keep it: that window
    // is where the listing itself goes up.
    private Step AfterSearch() => _mode == RunMode.ListUndercut ? Step.EnterListing : Step.CancelSell;

    private unsafe void CloseSearch()
    {
        var search = VisibleAddon(SearchResultAddonName);
        if (search is null)
        {
            _cancelFired = false;
            BeginStep(AfterSearch());
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

        var cfg = Plugin.Config.ListingHelper;
        // Only a lookup talks to the market board, so only a lookup needs its rate-limit spacing.
        var delay = _mode == RunMode.Lookup ? cfg.LookupDelayMs : cfg.DelayMs;
        _resumeAt = Environment.TickCount64 + Math.Max(0, delay);
        BeginStep(Step.OpenMenu);
    }

    private void RecordListings(MarketTarget target, IReadOnlyList<MarketListing> listings)
    {
        _cache[target] = new CachedListings([.. listings], Environment.TickCount64);
        if (Price(target) is not { } result)
            return;

        _panel.SetPrice(target, result);

        if (result.Outcome == UndercutOutcome.NoListings)
            RequestUniversalisPrice(target, true);
    }

    // Fired off and left to land on its own: the answer only fills a box in the panel, so the
    // run walks on to the next item rather than sitting on a web request.
    private void RequestUniversalisPrice(MarketTarget target, bool boardAnswered)
    {
        var cfg = Plugin.Config.ListingHelper;
        if (!cfg.UseUniversalisFallback)
            return;

        var (world, dataCentre) = HomeMarket();
        if (dataCentre.Length == 0)
        {
            Services.Log.Debug("Listing Helper: no home world to ask Universalis about.");
            return;
        }

        // Read here rather than in the continuation, so a setting changed while the request is
        // in the air cannot price the answer under something the run never used.
        var ignoreQuality = _running ? _ignoreQuality : cfg.IgnoreQuality;
        _ = FetchUniversalisPrice(
            target,
            new UniversalisRequest(
                cfg.UniversalisSource,
                ignoreQuality,
                cfg.UndercutGil,
                world,
                dataCentre,
                ActiveRetainerId(),
                boardAnswered),
            _web.Token);
    }

    private async Task FetchUniversalisPrice(
        MarketTarget target,
        UniversalisRequest request,
        CancellationToken token)
    {
        var item = await UniversalisClient.Fetch(target.ItemId, request.DataCentre, token).ConfigureAwait(false);
        if (item is null || token.IsCancellationRequested)
            return;

        var price = UniversalisFallback.Resolve(
            item,
            request.Source,
            target.HighQuality,
            request.IgnoreQuality,
            request.UndercutGil,
            request.World,
            request.DataCentre);

        if (price.Basis == UniversalisBasis.None)
        {
            Services.Log.Debug($"Listing Helper: Universalis had nothing for item {target.ItemId} either.");
            return;
        }

        await Services.Framework.RunOnFrameworkThread(() =>
        {
            // The panel drops its rows when another retainer is opened, so an answer that
            // outlived the one that asked for it has nowhere left to go.
            if (!token.IsCancellationRequested && ActiveRetainerId() == request.RetainerId)
                _panel.SetUniversalisPrice(target, price, request.BoardAnswered);
        }).ConfigureAwait(false);
    }

    private static (string World, string DataCentre) HomeMarket()
    {
        var player = Services.PlayerState;
        if (!player.IsLoaded || player.HomeWorld.ValueNullable is not { } world)
            return (string.Empty, string.Empty);

        return (world.Name.ExtractText(), world.DataCenter.ValueNullable?.Name.ExtractText() ?? string.Empty);
    }

    internal bool Cached(MarketTarget target) => Fresh(target) is not null;

    internal long? CachedAgeMs(MarketTarget target) =>
        Fresh(target) is not null ? Environment.TickCount64 - _cache[target].RecordedAt : null;

    // Dropped on the way past rather than on a timer, which is enough - the panel asks about
    // every row it draws, so a stale entry never outlives the frame it is noticed on.
    private List<MarketListing>? Fresh(MarketTarget target)
    {
        if (!_cache.TryGetValue(target, out var entry))
            return null;

        if (Environment.TickCount64 - entry.RecordedAt > CacheLifetimeMs)
        {
            _cache.Remove(target);
            return null;
        }

        return entry.Listings;
    }

    private UndercutResult? Price(MarketTarget target)
    {
        if (Fresh(target) is not { } listings)
            return null;

        var cfg = Plugin.Config.ListingHelper;

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
                ? $"Listing Helper: checked market prices for {_runLabel}."
                : $"Listing Helper: stopped checking prices - {reason}.");
            return;
        }

        if (_mode == RunMode.Remove)
        {
            Services.Chat.Print(reason is null
                ? $"Listing Helper: took {_runLabel} off the market and into {DuplicateSource.Describe(_returnTo)}."
                : $"Listing Helper: stopped after removing {Listings(_updated)} - {reason}.");
            return;
        }

        if (_mode == RunMode.Duplicate)
        {
            Services.Chat.Print(reason is null
                ? $"Listing Helper: put up {_runLabel}."
                : $"Listing Helper: stopped after putting up {Listings(_updated)} - {reason}.");
            return;
        }

        if (_mode == RunMode.ListUndercut)
        {
            Services.Chat.Print(reason is null
                ? $"Listing Helper: listed {_runLabel}."
                : $"Listing Helper: did not list {_runLabel} - {reason}.");
            return;
        }

        Services.Chat.Print(reason is null
            ? $"Listing Helper: updated {_runLabel}."
            : $"Listing Helper: stopped after {Listings(_updated)} - {reason}.");
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
        var ignoreQuality = Plugin.Config.ListingHelper.IgnoreQuality;
        var index = new Dictionary<MarketTarget, int>();
        var rank = new Dictionary<MarketTarget, int>();
        var order = SellListOrder(OccupiedSlots(container));

        for (var i = 0; i < container->Size; i++)
        {
            var item = container->GetInventorySlot(i);
            if (item is null || item->ItemId == 0)
                continue;

            var highQuality = !ignoreQuality && item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality);
            var target = new MarketTarget(ItemIdNormalizer.ToBaseItemId(item->ItemId), highQuality);
            var price = (int)Math.Clamp(manager->GetRetainerMarketPrice((short)i), 0, int.MaxValue);

            // Anything the window did not place keeps its slot order, behind everything it did.
            var displayRank = order.TryGetValue(i, out var display) ? display : MaxListings + i;

            if (index.TryGetValue(target, out var at))
            {
                var row = rows[at];
                rows[at] = row with
                {
                    Listings = row.Listings + 1,
                    Quantity = row.Quantity + item->Quantity,
                    MixedPrices = row.MixedPrices || row.CurrentPrice != price,
                };

                rank[target] = Math.Min(rank[target], displayRank);
                continue;
            }

            index[target] = rows.Count;
            rank[target] = displayRank;
            rows.Add(new PanelRow(target, ItemName(target.ItemId, highQuality), 1, item->Quantity, price, false));
        }

        // The window sorts by item category rather than by slot, so following the container
        // would list everything in an order the player is not looking at.
        rows.Sort((a, b) => rank[a.Target].CompareTo(rank[b.Target]));
        return rows;
    }

    private static unsafe int OccupiedSlots(InventoryContainer* container)
    {
        var occupied = 0;
        for (var i = 0; i < container->Size; i++)
        {
            var item = container->GetInventorySlot(i);
            if (item is not null && item->ItemId != 0)
                occupied++;
        }

        return occupied;
    }

    private static unsafe int OccupiedMarketSlots()
    {
        var container = MarketContainer();
        return container is null ? 0 : OccupiedSlots(container);
    }

    private static unsafe int MarketQuantity(MarketTarget target)
    {
        var total = 0;
        var container = MarketContainer();
        if (container is null)
            return total;

        for (var i = 0; i < container->Size; i++)
        {
            var item = container->GetInventorySlot(i);
            if (Matches(item, target, false))
                total += item->Quantity;
        }

        return total;
    }

    internal static unsafe bool SellListOpen() => LoadedAddon(SellListAddonName) is not null;

    // Maps market slot to the row the window draws it on. The offsets are lifted from a working
    // plugin rather than from a mapped struct, so a mismatched count is taken as "these are
    // wrong or not built yet" and the caller falls back to slot order.
    private static unsafe Dictionary<int, int> SellListOrder(int occupied)
    {
        var order = new Dictionary<int, int>();
        var addon = LoadedAddon(SellListAddonName);
        if (addon is null || addon->AtkValues is null)
            return order;

        for (var row = 0; row < MaxListings; row++)
        {
            var index = OrderBaseIndex + row * OrderStride;
            if (index >= addon->AtkValuesCount)
                break;

            var value = addon->AtkValues[index];
            if (value.Type == AtkValueType.Int)
                order.TryAdd(value.Int, row);
        }

        if (order.Count != occupied)
            order.Clear();

        return order;
    }

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

    private static unsafe int FirstSlot(MarketTarget target, bool ignoreQuality)
    {
        var slots = FindSlots(target, ignoreQuality);
        return slots.Count > 0 ? slots[0] : -1;
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

        Services.Log.Warning($"Listing Helper: {name} ignored its cancel callback, closing it directly.");
        addon->Close(true);
    }

    // A confirm that never landed leaves the sell window sitting on screen, and nothing else
    // takes it down once the run has given up.
    private static unsafe void CloseStrandedSell()
    {
        var sell = VisibleAddon(SellAddonName);
        if (sell is not null)
            sell->Close(true);
    }

    private static unsafe uint ReadItemDetailAgentItemId()
    {
        var agent = AgentItemDetail.Instance();
        return agent is null ? 0 : agent->ItemId;
    }

    internal static string ItemName(uint itemId, bool highQuality)
    {
        var name = ItemNames.Lookup(itemId);
        return highQuality ? $"{name} {HighQualityGlyph}" : name;
    }

    private static string Listings(int count) => count == 1 ? "1 listing" : $"{count} listings";

    private static string StepDescription(Step step) => step switch
    {
        Step.OpenMenu => "opening the listing's menu",
        Step.PickAdjustPrice => "picking Adjust Price",
        Step.EnterPrice => "waiting for the price window",
        Step.ComparePrices => "opening the market board",
        Step.AwaitListings => "waiting for market prices",
        Step.CloseSearch => "closing the market board",
        Step.SettleRemove => "waiting for the listing to come off",
        Step.PickPutUpForSale => "picking Put Up for Sale",
        Step.EnterListing => "waiting for the sell window",
        Step.SettleDuplicate => "waiting for the copy to go up",
        Step.SettleMerge => "merging the split stacks",
        _ => "closing the price window",
    };

    private static unsafe List<string> MenuEntries(AtkUnitBase* menu)
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

        return entries;
    }

    // The adjust-price entry is assumed to be first; logging what the menu actually held makes
    // a wrong pick obvious in the log instead of silent.
    private static unsafe void LogMenuEntries(AtkUnitBase* menu) =>
        Services.Log.Debug($"Listing Helper: context menu held [{string.Join(" | ", MenuEntries(menu))}]");

    public override void DrawConfig()
    {
        var cfg = Plugin.Config.ListingHelper;

        var showPanel = cfg.ShowPanel;
        if (ImGui.Checkbox("Show panel at the retainer sell list##ListingHelper", ref showPanel))
        {
            cfg.ShowPanel = showPanel;
            Plugin.Config.Save();
        }

        var dock = cfg.DockToSellList;
        if (ImGui.Checkbox("Dock it to the sell list##ListingHelper", ref dock))
        {
            cfg.DockToSellList = dock;
            Plugin.Config.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Pins the panel to the right of the retainer window and matches its height.");

        var lockSize = cfg.LockSize;
        if (ImGui.Checkbox("Lock the panel size##ListingHelper", ref lockSize))
        {
            cfg.LockSize = lockSize;
            Plugin.Config.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Stops the panel being resized by dragging its corner.");

        // Docking pins the panel itself, so this setting has nothing left to do while it is on.
        ImGui.BeginDisabled(cfg.DockToSellList);

        var lockPosition = cfg.LockPosition;
        if (ImGui.Checkbox("Lock the panel position##ListingHelper", ref lockPosition))
        {
            cfg.LockPosition = lockPosition;
            Plugin.Config.Save();
        }

        ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(cfg.DockToSellList
                ? "Docking already holds the panel in place."
                : "Stops the panel being dragged around.");
        }

        var ignoreQuality = cfg.IgnoreQuality;
        if (ImGui.Checkbox("Ignore NQ/HQ##ListingHelper", ref ignoreQuality))
        {
            cfg.IgnoreQuality = ignoreQuality;
            Plugin.Config.Save();

            // Rows are keyed on quality, so the two tiers merge or split under this and any
            // price already typed would land against the wrong one.
            _panel.Reset();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Treat both qualities of an item as one thing to price, instead of keeping them apart.");

        var mergeStacks = cfg.MergeSplitStacks;
        if (ImGui.Checkbox("Merge split stacks##ListingHelper", ref mergeStacks))
        {
            cfg.MergeSplitStacks = mergeStacks;
            Plugin.Config.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "A copy goes up out of one bag slot, so partial stacks spread over several are\n"
                + "poured together first. Off, a duplicate is refused instead of moving anything.");
        }

        var undercut = cfg.UndercutGil;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputInt("Undercut by (gil)##ListingHelper", ref undercut))
        {
            cfg.UndercutGil = Math.Clamp(undercut, 0, UndercutCalculator.MaxPrice);
            Plugin.Config.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Taken off the lowest listing found. Your own retainers are never undercut.");

        DrawUniversalisConfig(cfg);

        var delay = cfg.DelayMs;
        ImGui.SetNextItemWidth(160);
        if (ImGui.SliderInt("Delay between listings (ms)##ListingHelper", ref delay, 100, 3000))
        {
            cfg.DelayMs = delay;
            Plugin.Config.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Waited after each listing before the next one is repriced.");

        var lookupDelay = cfg.LookupDelayMs;
        ImGui.SetNextItemWidth(160);
        if (ImGui.SliderInt("Delay between price checks (ms)##ListingHelper", ref lookupDelay, 1000, 10000))
        {
            cfg.LookupDelayMs = lookupDelay;
            Plugin.Config.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Market board queries are rate limited by the server. Lower this at your own risk.");
    }

    private static void DrawUniversalisConfig(ListingHelperConfig cfg)
    {
        var fallback = cfg.UseUniversalisFallback;
        if (ImGui.Checkbox("Fall back to Universalis##ListingHelper", ref fallback))
        {
            cfg.UseUniversalisFallback = fallback;
            Plugin.Config.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "When nothing is listed here, ask universalis.app what the rest of your data centre\n"
                + "is doing with the item. Only items the board came back empty on are ever sent.");
        }

        ImGui.BeginDisabled(!cfg.UseUniversalisFallback);
        ImGui.SetNextItemWidth(280);

        if (ImGui.BeginCombo("Universalis price##ListingHelper", SourceLabel(cfg.UniversalisSource)))
        {
            foreach (var source in Enum.GetValues<UniversalisSource>())
            {
                if (ImGui.Selectable(SourceLabel(source), source == cfg.UniversalisSource))
                {
                    cfg.UniversalisSource = source;
                    Plugin.Config.Save();
                }

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(SourceTooltip(source));
            }

            ImGui.EndCombo();
        }

        ImGui.EndDisabled();
    }

    private static string SourceLabel(UniversalisSource source) => source switch
    {
        UniversalisSource.CheapestListing => "Cheapest listing on your data centre",
        UniversalisSource.SaleAverage => "Recent sale average",
        _ => "Cheapest listing, then sale average",
    };

    private static string SourceTooltip(UniversalisSource source) => source switch
    {
        UniversalisSource.CheapestListing =>
            "The lowest price anyone on your data centre is asking, undercut like any other\n"
            + "competitor. Nothing is filled in if the whole data centre is empty too.",
        UniversalisSource.SaleAverage =>
            "What the item actually went for recently on your world, taken as it stands - there\n"
            + "is nobody to undercut. Falls back to the whole data centre if your world has no\n"
            + "recent sales.",
        _ =>
            "The lowest listing on your data centre if there is one, and the recent sale average\n"
            + "if there is not.",
    };
}
