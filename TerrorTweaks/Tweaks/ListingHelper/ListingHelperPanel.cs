using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;

namespace TerrorTweaks.Tweaks.ListingHelper;

internal sealed class ListingHelperPanel
{
    private const float MinDockedWidth = 320f;
    private const float MinDockedHeight = 140f;
    private const float DefaultHeight = 380f;
    private const float DockOffsetY = 25f;
    private const float PriceColumnWidth = 80f;
    private const float ActionColumnWidth = 120f;
    private const float SettingsButtonWidth = 90f;
    private const int LookupOverheadMs = 500;

    private static readonly Vector4 Muted   = new(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Vector4 Warning = new(1f, 0.8f, 0.35f, 1f);
    private static readonly Vector4 Info    = new(0.55f, 0.8f, 1f, 1f);
    private static readonly Vector4 Outside = new(0.78f, 0.66f, 1f, 1f);

    private static readonly Vector4 Green        = new(0.22f, 0.50f, 0.25f, 1f);
    private static readonly Vector4 GreenHovered = new(0.28f, 0.62f, 0.31f, 1f);
    private static readonly Vector4 GreenActive  = new(0.18f, 0.42f, 0.20f, 1f);

    private readonly Dictionary<MarketTarget, int> _inputs = [];
    private readonly Dictionary<MarketTarget, UndercutOutcome> _outcomes = [];

    // Where a Universalis price came from, kept per row because the tooltip is the only place
    // that says the number is not off this world's board.
    private readonly Dictionary<MarketTarget, string> _notes = [];

    private readonly ListingHelperTweak _tweak;

    private ulong _retainerId;

    internal ListingHelperPanel(ListingHelperTweak tweak)
    {
        _tweak = tweak;
    }

    internal void SetPrice(MarketTarget target, UndercutResult result)
    {
        _outcomes[target] = result.Outcome;
        _notes.Remove(target);

        if (result.Outcome != UndercutOutcome.NoListings)
            _inputs[target] = result.Price;
    }

    internal void SetUniversalisPrice(MarketTarget target, UniversalisPrice price, bool boardAnswered)
    {
        _outcomes[target] = UndercutOutcome.Universalis;
        _inputs[target] = price.Price;

        // A board that never answered is not the same as one with nothing on it, and only one of
        // the two means the item is genuinely unsold here.
        var reason = boardAnswered ? "Nothing listed here." : "The market board did not answer.";
        _notes[target] = price.Basis == UniversalisBasis.Listing
            ? $"{reason} Universalis has one on {price.Scope}."
            : $"{reason} Universalis says it last sold on {price.Scope} for about this.";
    }

    internal void Reset()
    {
        _inputs.Clear();
        _outcomes.Clear();
        _notes.Clear();
    }

    internal void Draw()
    {
        if (!Plugin.Config.ListingHelper.ShowPanel || !ListingHelperTweak.SellListOpen())
            return;

        // Two retainers holding the same item would otherwise inherit each other's boxes, and
        // Apply All would reprice the second one to a number nobody looked at.
        var retainerId = ListingHelperTweak.ActiveRetainerId();
        if (retainerId != _retainerId)
        {
            _retainerId = retainerId;
            Reset();
        }

        var rows = ListingHelperTweak.Rows();
        Prune(rows);

        var cfg = Plugin.Config.ListingHelper;

        // The table does the scrolling; the window around it never should, or the two nest and
        // the buttons get pushed out of reach.
        var flags = ImGuiWindowFlags.NoTitleBar
                    | ImGuiWindowFlags.NoScrollbar
                    | ImGuiWindowFlags.NoScrollWithMouse
                    | Dock();

        if (cfg.LockSize)
            flags |= ImGuiWindowFlags.NoResize;

        // Docking already returns NoMove, so this only matters while the panel floats free.
        if (cfg.LockPosition)
            flags |= ImGuiWindowFlags.NoMove;

        ImGui.SetNextWindowSize(new Vector2(520, DefaultHeight), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Listing Helper##TerrorTweaksListingHelper", flags))
        {
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted("Terror Tweaks Listing Helper");
        ImGui.Separator();

        if (rows.Count == 0)
            ImGui.TextColored(Muted, "This retainer has nothing on the market.");
        else
            DrawTable(rows);

        ImGui.Separator();
        DrawButtons(rows);

        ImGui.End();
    }

    // Pinned rather than remembered, so the panel follows the sell list around the screen. The
    // offset is hand-tuned: the addon reports a top edge above where its frame actually draws.
    private static ImGuiWindowFlags Dock()
    {
        if (!Plugin.Config.ListingHelper.DockToSellList)
            return ImGuiWindowFlags.None;

        if (ListingHelperTweak.SellListBounds() is not { } bounds)
            return ImGuiWindowFlags.None;

        ImGui.SetNextWindowPos(
            new Vector2(bounds.X + bounds.Width, bounds.Y + DockOffsetY),
            ImGuiCond.Always);

        ImGui.SetNextWindowSizeConstraints(
            new Vector2(MinDockedWidth, MinDockedHeight),
            new Vector2(float.MaxValue, Math.Max(MinDockedHeight, bounds.Height)));

        return ImGuiWindowFlags.NoMove;
    }

    // A listing that sold out and later came back would otherwise reappear holding the price
    // and the verdict it had before it went away.
    private void Prune(List<PanelRow> rows)
    {
        if (_inputs.Count == 0 && _outcomes.Count == 0)
            return;

        var live = new HashSet<MarketTarget>(rows.Count);
        foreach (var row in rows)
            live.Add(row.Target);

        foreach (var target in _inputs.Keys.Where(target => !live.Contains(target)).ToList())
            _inputs.Remove(target);

        foreach (var target in _outcomes.Keys.Where(target => !live.Contains(target)).ToList())
            _outcomes.Remove(target);

        foreach (var target in _notes.Keys.Where(target => !live.Contains(target)).ToList())
            _notes.Remove(target);
    }

    private void DrawTable(List<PanelRow> rows)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH
            | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable;
        // Whatever the table does not take is what the separator and button row get, so this is
        // the one place the height of the strip along the bottom is set.
        var height = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing();

        // Floored at a single row rather than a fixed size, so a short window shrinks the table
        // instead of overflowing and handing the scrollbar back to the window.
        var outer = new Vector2(0, Math.Max(ImGui.GetTextLineHeightWithSpacing(), height));

        if (!ImGui.BeginTable("##ListingHelperRows", 4, flags, outer))
            return;

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Now", ImGuiTableColumnFlags.WidthFixed, PriceColumnWidth);
        ImGui.TableSetupColumn("New", ImGuiTableColumnFlags.WidthFixed, PriceColumnWidth);
        ImGui.TableSetupColumn("##Update", ImGuiTableColumnFlags.WidthFixed, ActionColumnWidth);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        var busy = _tweak.IsRunning;
        foreach (var row in rows)
            DrawRow(row, busy);

        ImGui.EndTable();
    }

    private void DrawRow(PanelRow row, bool busy)
    {
        var id = $"{row.Target.ItemId}{(row.Target.HighQuality ? "hq" : "nq")}";

        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        var outcome = _outcomes.TryGetValue(row.Target, out var found) ? found : (UndercutOutcome?)null;
        var label = row.Listings > 1 ? $"{row.Name} x{row.Listings}" : row.Name;

        if (OutcomeColour(outcome) is { } colour)
            ImGui.TextColored(colour, label);
        else
            ImGui.TextUnformatted(label);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Tooltip(row, outcome));

        ImGui.TableNextColumn();
        if (row.MixedPrices)
            ImGui.TextColored(Muted, "mixed");
        else
            ImGui.TextUnformatted($"{row.CurrentPrice:N0}");

        ImGui.TableNextColumn();
        var price = Wanted(row);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputInt($"##price{id}", ref price, 0))
            _inputs[row.Target] = Math.Clamp(price, UndercutCalculator.MinPrice, UndercutCalculator.MaxPrice);

        ImGui.TableNextColumn();

        // Green marks an item this session already holds listings for - the button throws them
        // away and asks again, so it is worth seeing which ones there is something to throw.
        var cached = _tweak.Cached(row.Target);
        PushGreen(cached);

        ImGui.BeginDisabled(busy);
        if (ImGuiComponents.IconButton($"##update{id}", FontAwesomeIcon.SyncAlt))
            _tweak.RequestPrices([row.Target]);
        ImGui.EndDisabled();

        PopGreen(cached);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Update price from MB");

        ImGui.SameLine();

        // Green once the listings already sit at the number in the box, so a row with nothing
        // left to do reads as done without pressing it.
        var applied = Matches(row);
        PushGreen(applied);

        ImGui.BeginDisabled(busy);
        if (ImGuiComponents.IconButton($"##apply{id}", FontAwesomeIcon.Check))
            _tweak.Apply(row.Target, Wanted(row));
        ImGui.EndDisabled();

        PopGreen(applied);

        ImGui.SameLine();

        // Held rather than clicked outright - this pulls real listings off the market, and the
        // button sits one row away from the one you press repeatedly.
        var io = ImGui.GetIO();
        var armed = io.KeyCtrl;
        var toRetainer = armed && io.KeyShift;

        ImGui.BeginDisabled(busy || !armed);
        if (ImGuiComponents.IconButton($"##remove{id}", FontAwesomeIcon.Trash))
            _tweak.RemoveListings(row.Target, toRetainer ? BagSide.Retainer : BagSide.Player);
        ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(RemoveTooltip(row, armed, toRetainer));
    }

    private static string RemoveTooltip(PanelRow row, bool armed, bool toRetainer)
    {
        if (!armed)
            return "Hold CTRL to take this off the market, or CTRL+SHIFT to send it to the retainer.";

        var what = row.Listings == 1 ? "this listing" : $"all {row.Listings} listings";
        return toRetainer
            ? $"Take {what} off the market and into the retainer's inventory."
            : $"Take {what} off the market and into your inventory. Hold SHIFT as well for the retainer's.";
    }

    private int Wanted(PanelRow row) =>
        _inputs.TryGetValue(row.Target, out var stored) ? stored : row.CurrentPrice;

    // Listings sitting at different prices are never all at the box price, whatever it reads.
    private bool Matches(PanelRow row) => !row.MixedPrices && row.CurrentPrice == Wanted(row);

    // Anything already stored is left alone, so a run only fills in the gaps - until there are
    // none left, when the caller asks for the lot instead.
    private IEnumerable<MarketTarget> Pending(List<PanelRow> rows, bool all) =>
        rows.Where(row => all || !_tweak.Cached(row.Target)).Select(row => row.Target);

    private static void PushGreen(bool green)
    {
        if (!green)
            return;

        ImGui.PushStyleColor(ImGuiCol.Button, Green);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, GreenHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, GreenActive);
    }

    private static void PopGreen(bool green)
    {
        if (green)
            ImGui.PopStyleColor(3);
    }

    // Carried by the item's colour and its tooltip rather than a line of its own, so a verdict
    // never changes the height of a row.
    private static Vector4? OutcomeColour(UndercutOutcome? outcome) => outcome switch
    {
        UndercutOutcome.NoListings => Warning,
        UndercutOutcome.HeldAtOwn => Info,
        UndercutOutcome.Universalis => Outside,
        _ => null,
    };

    private string Tooltip(PanelRow row, UndercutOutcome? outcome)
    {
        var listings = row.Listings == 1 ? "1 listing" : $"{row.Listings} listings";
        var text = $"{row.Quantity:N0} in {listings}";

        return outcome switch
        {
            UndercutOutcome.NoListings => $"{text}\nNothing listed on the market to undercut.",
            UndercutOutcome.HeldAtOwn => $"{text}\nYour own retainer is the lowest, so the price was left alone.",
            UndercutOutcome.Universalis when _notes.TryGetValue(row.Target, out var note) => $"{text}\n{note}",
            _ => text,
        };
    }

    private void DrawButtons(List<PanelRow> rows)
    {
        if (_tweak.IsRunning)
        {
            if (ImGui.Button("Stop##ListingHelperPanel", new Vector2(110, 0)))
                _tweak.Stop();

            ImGui.SameLine();
            ImGui.TextColored(Muted, _tweak.Status);
        }
        else
        {
            ImGui.BeginDisabled(rows.Count == 0);

            // With nothing left to fill in, the button turns into a full refresh - and goes
            // green to say so, rather than looking like it has stopped doing anything.
            var refreshAll = rows.Count > 0 && rows.All(row => _tweak.Cached(row.Target));
            PushGreen(refreshAll);

            if (ImGui.Button("Update All##ListingHelperPanel", new Vector2(110, 0)))
                _tweak.RequestPrices([.. Pending(rows, refreshAll)]);

            PopGreen(refreshAll);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(refreshAll
                    ? $"Every item is stored already. Checks them all again, about {Estimate(rows, true)} at the current delay."
                    : $"Checks every item not looked up yet, about {Estimate(rows, false)} at the current delay.");
            }

            ImGui.SameLine();

            // Green once every listing already sits at the number in its box, so a finished
            // pass is visible without pressing the button to find out there is nothing to do.
            var applied = rows.Count > 0 && rows.All(Matches);
            PushGreen(applied);

            if (ImGui.Button("Apply All##ListingHelperPanel", new Vector2(110, 0)))
                _tweak.ApplyAll(_inputs);

            PopGreen(applied);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(applied
                    ? "Every listing is already at the value in its box."
                    : "Reprices every listing to the value in its box, skipping ones already at it.");
            }

            ImGui.EndDisabled();
        }

        DrawSettingsButton();
    }

    private static void DrawSettingsButton()
    {
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - SettingsButtonWidth - ImGui.GetStyle().WindowPadding.X);

        if (ImGui.Button("Settings##ListingHelperPanel", new Vector2(SettingsButtonWidth, 0)))
            Plugin.OpenConfig();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Open the TerrorTweaks settings window.");
    }

    // One lookup per item however many listings sit behind it, each paying the configured delay
    // plus a flat allowance for the sell window and the server round trip.
    private string Estimate(List<PanelRow> rows, bool all)
    {
        var pending = Pending(rows, all).Count();
        var seconds = (int)Math.Ceiling(pending * (Plugin.Config.ListingHelper.LookupDelayMs + LookupOverheadMs) / 1000.0);
        return seconds < 60 ? $"{seconds}s" : $"{seconds / 60}m {seconds % 60}s";
    }
}
