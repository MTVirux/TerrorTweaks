using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using TerrorTweaks.Framework;

namespace TerrorTweaks.UI;

internal sealed class ConfigWindow : IDisposable
{
    private readonly TweakManager _tweakManager;
    private bool _isOpen;

    internal ConfigWindow(TweakManager tweakManager)
    {
        _tweakManager = tweakManager;
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

        ImGui.SetNextWindowSize(new Vector2(440, 360), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("TerrorTweaks — Configuration", ref _isOpen))
        {
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted("Tweaks");
        ImGui.Separator();

        foreach (var tweak in _tweakManager.Tweaks)
        {
            var enabled = tweak.Enabled;
            if (ImGui.Checkbox($"{tweak.Name}##{tweak.InternalName}", ref enabled))
                _tweakManager.SetEnabled(tweak, enabled);

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(tweak.Description);

            if (tweak.Enabled)
            {
                ImGui.Indent();
                tweak.DrawConfig();
                ImGui.Unindent();
            }
        }

        ImGui.End();
    }

    public void Dispose()
    {
    }
}
