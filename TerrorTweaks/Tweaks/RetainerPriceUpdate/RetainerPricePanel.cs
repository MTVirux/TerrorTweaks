using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace TerrorTweaks.Tweaks.RetainerPriceUpdate;

internal sealed class RetainerPricePanel
{
    private const float MinDockedWidth = 320f;
    private const float MinDockedHeight = 140f;
    private const float DefaultHeight = 380f;
    private const float DockOffsetY = 25f;
    private const float PriceColumnWidth = 80f;
    private const float SettingsButtonWidth = 90f;

    private static readonly Vector4 Muted   = new(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Vector4 Warning = new(1f, 0.8f, 0.35f, 1f);
    private static readonly Vector4 Info    = new(0.55f, 0.8f, 1f, 1f);

    private readonly Dictionary<MarketTarget, int> _inputs = [];
    private readonly Dictionary<MarketTarget, UndercutOutcome> _outcomes = [];
    private readonly RetainerPriceUpdateTweak _tweak;

    private ulong _retainerId;

    internal RetainerPricePanel(RetainerPriceUpdateTweak tweak)
    {
        _tweak = tweak;
    }

    internal void SetPrice(MarketTarget target, UndercutResult result)
    {
        _outcomes[target] = result.Outcome;
        if (result.Outcome != UndercutOutcome.NoListings)
            _inputs[target] = result.Price;
    }

    internal void Reset()
    {
        _inputs.Clear();
        _outcomes.Clear();
    }

    internal void Draw()
    {
        if (!Plugin.Config.RetainerPrice.ShowPanel || !RetainerPriceUpdateTweak.SellListOpen())
            return;

        // Two retainers holding the same item would otherwise inherit each other's boxes, and
        // Apply All would reprice the second one to a number nobody looked at.
        var retainerId = RetainerPriceUpdateTweak.ActiveRetainerId();
        if (retainerId != _retainerId)
        {
            _retainerId = retainerId;
            Reset();
        }

        var rows = RetainerPriceUpdateTweak.Rows();
        Prune(rows);

        var cfg = Plugin.Config.RetainerPrice;
        var flags = ImGuiWindowFlags.NoTitleBar | Dock();

        if (cfg.LockSize)
            flags |= ImGuiWindowFlags.NoResize;

        // Docking already returns NoMove, so this only matters while the panel floats free.
        if (cfg.LockPosition)
            flags |= ImGuiWindowFlags.NoMove;

        ImGui.SetNextWindowSize(new Vector2(520, DefaultHeight), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Retainer Price##TerrorTweaksRetainerPrice", flags))
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
        if (!Plugin.Config.RetainerPrice.DockToSellList)
            return ImGuiWindowFlags.None;

        if (RetainerPriceUpdateTweak.SellListBounds() is not { } bounds)
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
    }

    private void DrawTable(List<PanelRow> rows)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY;
        // Whatever the table does not take is what the separator and button row get, so this is
        // the one place the height of the strip along the bottom is set.
        var height = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing();

        if (!ImGui.BeginTable("##RetainerPriceRows", 4, flags, new Vector2(0, Math.Max(80, height))))
            return;

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Now", ImGuiTableColumnFlags.WidthFixed, PriceColumnWidth);
        ImGui.TableSetupColumn("New", ImGuiTableColumnFlags.WidthFixed, PriceColumnWidth);
        ImGui.TableSetupColumn("##Update", ImGuiTableColumnFlags.WidthFixed, 70);
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
        var price = _inputs.TryGetValue(row.Target, out var stored) ? stored : row.CurrentPrice;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputInt($"##price{id}", ref price, 0))
            _inputs[row.Target] = Math.Clamp(price, UndercutCalculator.MinPrice, UndercutCalculator.MaxPrice);

        ImGui.TableNextColumn();
        ImGui.BeginDisabled(busy);
        if (ImGui.Button($"Update##{id}"))
            _tweak.RequestPrices([row.Target]);
        ImGui.EndDisabled();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Check the market board for this item and fill in an undercut price.");
    }

    // Carried by the item's colour and its tooltip rather than a line of its own, so a verdict
    // never changes the height of a row.
    private static Vector4? OutcomeColour(UndercutOutcome? outcome) => outcome switch
    {
        UndercutOutcome.NoListings => Warning,
        UndercutOutcome.HeldAtOwn => Info,
        _ => null,
    };

    private static string Tooltip(PanelRow row, UndercutOutcome? outcome)
    {
        var listings = row.Listings == 1 ? "1 listing" : $"{row.Listings} listings";
        var text = $"{row.Quantity:N0} in {listings}";

        return outcome switch
        {
            UndercutOutcome.NoListings => $"{text}\nNothing listed on the market to undercut.",
            UndercutOutcome.HeldAtOwn => $"{text}\nYour own retainer is the lowest, so the price was left alone.",
            _ => text,
        };
    }

    private void DrawButtons(List<PanelRow> rows)
    {
        if (_tweak.IsRunning)
        {
            if (ImGui.Button("Stop##RetainerPricePanel", new Vector2(110, 0)))
                _tweak.Stop();

            ImGui.SameLine();
            ImGui.TextColored(Muted, _tweak.Status);
        }
        else
        {
            ImGui.BeginDisabled(rows.Count == 0);

            if (ImGui.Button("Update All##RetainerPricePanel", new Vector2(110, 0)))
                _tweak.RequestPrices([.. rows.Select(row => row.Target)]);

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Checks every item not looked up yet, about {Estimate(rows)} at the current delay.");

            ImGui.SameLine();
            if (ImGui.Button("Apply All##RetainerPricePanel", new Vector2(110, 0)))
                _tweak.ApplyAll(_inputs);

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Reprices every listing to the value in its box, skipping ones already at it.");

            ImGui.EndDisabled();
        }

        DrawSettingsButton();
    }

    private static void DrawSettingsButton()
    {
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - SettingsButtonWidth - ImGui.GetStyle().WindowPadding.X);

        if (ImGui.Button("Settings##RetainerPricePanel", new Vector2(SettingsButtonWidth, 0)))
            Plugin.OpenConfig();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Open the TerrorTweaks settings window.");
    }

    // Each lookup pays the configured delay plus the sell window and the server round trip.
    private string Estimate(List<PanelRow> rows)
    {
        var pending = rows.Count(row => _tweak.Price(row.Target) is null);
        if (pending == 0)
            return "nothing left to check";

        var seconds = (int)Math.Ceiling(pending * (Plugin.Config.RetainerPrice.LookupDelayMs + 4000) / 1000.0);
        return seconds < 60 ? $"{seconds}s" : $"{seconds / 60}m {seconds % 60}s";
    }
}
