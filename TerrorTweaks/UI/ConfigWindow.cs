using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using TerrorTweaks.Framework;

namespace TerrorTweaks.UI;

internal sealed class ConfigWindow : IDisposable
{
    private const float DefaultListWidth = 180f;

    private readonly TweakManager _tweakManager;
    private bool _isOpen;
    private string _search = string.Empty;
    private Tweak? _selected;
    private bool _editingOrder;
    private (int From, int To)? _pendingMove;

    internal ConfigWindow(TweakManager tweakManager)
    {
        _tweakManager = tweakManager;
        _selected = _tweakManager.Tweaks.FirstOrDefault();
    }

    internal bool IsOpen
    {
        get => _isOpen;
        set => _isOpen = value;
    }

    internal void Toggle() => _isOpen = !_isOpen;

    internal void Draw()
    {
        if (!_isOpen)
            return;

        ImGui.SetNextWindowSize(new Vector2(640, 400), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(460, 260), new Vector2(float.MaxValue, float.MaxValue));

        if (!ImGui.Begin("TerrorTweaks - Configuration", ref _isOpen))
        {
            ImGui.End();
            return;
        }

        DrawBody();

        ImGui.End();
    }

    // A table rather than two plain children, so the column border doubles as a drag handle and
    // ImGui remembers the width it was dragged to.
    private void DrawBody()
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV;
        var height = ImGui.GetContentRegionAvail().Y - (ImGui.GetStyle().CellPadding.Y * 2);

        if (!ImGui.BeginTable("##TweakLayout", 2, flags))
            return;

        ImGui.TableSetupColumn("##List", ImGuiTableColumnFlags.WidthFixed, DefaultListWidth);
        ImGui.TableSetupColumn("##Options", ImGuiTableColumnFlags.WidthStretch);

        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        DrawTweakList(height);

        ImGui.TableNextColumn();
        DrawTweakOptions(height);

        ImGui.EndTable();
    }

    private void DrawTweakList(float height)
    {
        ImGui.BeginChild("##TweakList", new Vector2(0, height), true);

        // A filtered list can't express a full ordering, so searching switches reordering off.
        var searching = _search.Length > 0;
        if (searching)
            _editingOrder = false;

        ImGui.BeginDisabled(searching);
        ImGui.Checkbox("Edit order", ref _editingOrder);
        ImGui.EndDisabled();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##TweakSearch", "Search...", ref _search, 64);
        ImGui.Separator();

        var tweaks = _tweakManager.Tweaks;
        for (var i = 0; i < tweaks.Count; i++)
        {
            var tweak = tweaks[i];
            if (searching && !tweak.Name.Contains(_search, StringComparison.OrdinalIgnoreCase))
                continue;

            var enabled = tweak.Enabled;
            if (ImGui.Checkbox($"##Enable{tweak.InternalName}", ref enabled))
                _tweakManager.SetEnabled(tweak, enabled);

            // The name is a separate hit target so picking a tweak to configure never
            // toggles it, and toggling never moves the selection.
            ImGui.SameLine();
            if (ImGui.Selectable($"{tweak.Name}##Select{tweak.InternalName}", _selected == tweak))
            {
                _selected = tweak;
                _tweakManager.MarkSeen(tweak);
            }

            if (_editingOrder)
                TrackDrag(i);

            if (_tweakManager.IsNew(tweak))
                DrawNewBadge();
        }

        ApplyPendingMove();

        ImGui.EndChild();
    }

    // Held-and-dragged off its own row means the row wants to swap with its neighbour. The move
    // is deferred to the end of the frame so the list isn't reordered while it is being drawn.
    private void TrackDrag(int index)
    {
        if (!ImGui.IsItemActive() || ImGui.IsItemHovered())
            return;

        var target = index + (ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Y < 0f ? -1 : 1);
        if (target < 0 || target >= _tweakManager.Tweaks.Count)
            return;

        _pendingMove = (index, target);
        ImGui.ResetMouseDragDelta();
    }

    private void ApplyPendingMove()
    {
        if (_pendingMove is not { } move)
            return;

        _tweakManager.MoveTweak(move.From, move.To);
        _pendingMove = null;
    }

    private static void DrawNewBadge()
    {
        const string label = "NEW";
        var width = ImGui.CalcTextSize(label).X;

        ImGui.SameLine(ImGui.GetContentRegionMax().X - width);
        ImGui.TextColored(RainbowColor(), label);
    }

    private static Vector4 RainbowColor()
    {
        var hue = (float)(ImGui.GetTime() * 0.4 % 1.0);
        float r = 0, g = 0, b = 0;

        ImGui.ColorConvertHSVtoRGB(hue, 0.8f, 1f, ref r, ref g, ref b);
        return new Vector4(r, g, b, 1f);
    }

    private void DrawTweakOptions(float height)
    {
        ImGui.BeginChild("##TweakOptions", new Vector2(0, height), true);

        if (_selected is not { } tweak)
        {
            ImGui.TextDisabled("No tweak selected.");
            ImGui.EndChild();
            return;
        }

        ImGui.TextUnformatted(tweak.Name);
        ImGui.TextWrapped(tweak.Description);
        ImGui.Separator();

        if (!tweak.HasConfig)
        {
            ImGui.TextDisabled("This tweak has no options.");
            ImGui.EndChild();
            return;
        }

        // Options stay on screen while the tweak is off so you can see what it offers
        // before enabling it.
        ImGui.BeginDisabled(!tweak.Enabled);
        tweak.DrawConfig();
        ImGui.EndDisabled();

        ImGui.EndChild();
    }

    public void Dispose()
    {
    }
}
