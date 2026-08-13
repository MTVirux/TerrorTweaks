using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace TerrorTweaks.Tweaks.BulkPurchase;

internal sealed class BulkPurchaseWindow
{
    private static readonly Vector4 Warning = new(1f, 0.8f, 0.35f, 1f);
    private static readonly Vector4 Error   = new(1f, 0.4f, 0.4f, 1f);

    private readonly BulkPurchaseTweak _tweak;

    private bool _isOpen;
    private ShopItemSnapshot _item;
    private int _quantity = 99;

    internal BulkPurchaseWindow(BulkPurchaseTweak tweak)
    {
        _tweak = tweak;
    }

    internal void Open(ShopItemSnapshot item)
    {
        _item = item;
        _isOpen = true;
    }

    internal void Close() => _isOpen = false;

    internal void Draw()
    {
        if (!_isOpen)
            return;

        // The shop is the source of truth: when the item stops being listed there is nothing
        // left to buy, so the window closes rather than showing stale prices.
        if (_tweak.Refresh(_item.ItemId) is not { } live)
        {
            _isOpen = false;
            return;
        }

        _item = live;

        ImGui.SetNextWindowSize(new Vector2(380, 300), ImGuiCond.FirstUseEver);
        var visible = ImGui.Begin("Bulk Purchase##TerrorTweaksBulkPurchase", ref _isOpen);

        // Closing the window must not leave a job running with no way to reach the Stop
        // button, so the close box doubles as a cancel.
        if (!_isOpen)
            _tweak.Stop();

        if (!visible)
        {
            ImGui.End();
            return;
        }

        var gil = BulkPurchaseTweak.Gil;
        var freeSlots = BulkPurchaseTweak.FreeSlots;

        ImGui.TextUnformatted(_item.Name);
        ImGui.Separator();
        ImGui.TextUnformatted($"{_item.UnitPrice:N0} gil each, you own {_item.Owned:N0}");
        ImGui.TextUnformatted($"{gil:N0} gil, {freeSlots} free bag slots");
        ImGui.Separator();

        if (_tweak.IsRunning)
            DrawProgress();
        else
            DrawForm(gil, freeSlots);

        ImGui.End();
    }

    private void DrawForm(long gil, int freeSlots)
    {
        var cfg = Plugin.Config.BulkPurchase;

        // Two checkboxes acting as a pair: ticking one clears the other, and un-ticking the
        // active one does nothing so a mode is always selected.
        var exact = !cfg.TopUpMode;
        if (ImGui.Checkbox("Buy this many##BulkPurchaseExact", ref exact) && exact)
        {
            cfg.TopUpMode = false;
            Plugin.Config.Save();
        }

        var topUp = cfg.TopUpMode;
        if (ImGui.Checkbox("Top up to this many owned##BulkPurchaseTopUp", ref topUp) && topUp)
        {
            cfg.TopUpMode = true;
            Plugin.Config.Save();
        }

        ImGui.SetNextItemWidth(140);
        if (ImGui.InputInt("Quantity##BulkPurchase", ref _quantity))
            _quantity = Math.Clamp(_quantity, 0, 999_999);

        var amount = BulkPurchasePlan.Resolve(_quantity, _item.Owned, cfg.TopUpMode);
        var plan = BulkPurchasePlan.Build(amount, _item.UnitPrice, _item.StackSize, gil, freeSlots);

        ImGui.Separator();
        ImGui.TextUnformatted($"{plan.Amount:N0} items in {plan.Purchases:N0} purchases");
        ImGui.TextUnformatted($"{plan.TotalCost:N0} gil, about {Estimate(plan.Purchases, cfg.DelayMs)}");

        if (plan.Block == PurchaseBlock.NotEnoughGil)
            ImGui.TextColored(Error, "Not enough gil.");
        else if (plan.SpaceWarning)
            ImGui.TextColored(Warning, $"May need up to {plan.SlotsNeeded} free slots, you have {freeSlots}.");

        ImGui.Separator();

        ImGui.BeginDisabled(!plan.CanStart);
        if (ImGui.Button("Buy##BulkPurchase", new Vector2(90, 0)))
            _tweak.Start(_item, plan.Amount);
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel##BulkPurchase", new Vector2(90, 0)))
            _isOpen = false;
    }

    private void DrawProgress()
    {
        var total = _tweak.Total;
        var bought = total - Math.Max(0, _tweak.Remaining);
        var fraction = total <= 0 ? 0f : (float)bought / total;

        ImGui.ProgressBar(fraction, new Vector2(-1, 0), $"{bought:N0} / {total:N0}");

        if (ImGui.Button("Stop##BulkPurchase", new Vector2(90, 0)))
            _tweak.Stop();
    }

    // Each purchase costs the configured delay plus roughly a server round trip.
    private static string Estimate(int purchases, int delayMs)
    {
        var seconds = (int)Math.Ceiling(purchases * (delayMs + 200) / 1000.0);
        return seconds < 60 ? $"{seconds}s" : $"{seconds / 60}m {seconds % 60}s";
    }
}
