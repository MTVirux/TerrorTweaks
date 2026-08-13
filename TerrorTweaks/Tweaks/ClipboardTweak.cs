using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text.ReadOnly;
using TerrorTweaks.Framework;
using TerrorTweaks.Util;

namespace TerrorTweaks.Tweaks;

public sealed class ClipboardTweak : Tweak
{
    private const string EntryName = "Clipboard";
    private const string MenuAddonName = "ContextMenu";

    // The menu can only be read once the game has built it, which happens after every plugin
    // has had its say, so the scan waits for the addon to show up.
    private const int ScanFrameLimit = 10;

    public override string Name => "Clipboard";

    public override string Description =>
        "Adds a \"Clipboard\" entry to item context menus that copies the item's name.";

    // Default-type menus only expose the hovered item through AgentItemDetail; restrict to
    // surfaces where that agent is reliably populated. Extend this list to add coverage.
    private static readonly string[] DefaultAddonAllowlist =
        ["ItemSearch", "ChatLog", "RecipeNote", "GatheringNote"];

    private string? _pendingMenu;
    private bool _pendingInjected;
    private int _pendingFrames;
    private bool _scanning;
    private string _newMatch = string.Empty;

    public override void Enable()
    {
        base.Enable();
        Services.ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    public override void Disable()
    {
        Services.ContextMenu.OnMenuOpened -= OnMenuOpened;
        StopScan();
        base.Disable();
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        var itemId = ResolveItemId(args);
        if (itemId == 0)
            return;

        var name = ItemNames.Lookup(itemId);
        if (string.IsNullOrEmpty(name))
            return;

        var menu = MenuKey(args);
        var inject = !Plugin.Config.Clipboard.SuppressedMenus.Contains(menu);

        if (inject)
        {
            args.AddMenuItem(new MenuItem
            {
                Name = EntryName,
                PrefixChar = 'T',
                OnClicked = _ => Copy(name),
            });
        }

        BeginScan(menu, inject);
    }

    private static string MenuKey(IMenuOpenedArgs args) => $"{args.MenuType}|{args.AddonName ?? "?"}";

    private void BeginScan(string menu, bool injected)
    {
        _pendingMenu = menu;
        _pendingInjected = injected;
        _pendingFrames = 0;

        if (_scanning)
            return;

        Services.Framework.Update += OnFrameworkUpdate;
        _scanning = true;
    }

    private void StopScan()
    {
        if (_scanning)
            Services.Framework.Update -= OnFrameworkUpdate;

        _scanning = false;
        _pendingMenu = null;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_pendingMenu is not { } menu)
        {
            StopScan();
            return;
        }

        var raw = ReadMenuEntries();
        if (raw.Count == 0)
        {
            if (++_pendingFrames < ScanFrameLimit)
                return;

            StopScan();
            return;
        }

        Evaluate(menu, raw);
        StopScan();
    }

    private void Evaluate(string menu, List<string> raw)
    {
        var cfg = Plugin.Config.Clipboard;
        var entries = ClipboardSuppression.Read(raw, _pendingInjected ? EntryName : null);

        var changed = ClipboardSuppression.Learn(cfg.LearnedEntries, entries);

        // Re-decided on every open rather than remembered once, so a menu stops being
        // suppressed as soon as whatever was matching it is gone.
        changed |= ClipboardSuppression.ShouldSuppress(entries, cfg.SuppressWhenPresent)
            ? cfg.SuppressedMenus.Add(menu)
            : cfg.SuppressedMenus.Remove(menu);

        if (changed)
            Plugin.Config.Save();
    }

    private static unsafe List<string> ReadMenuEntries()
    {
        var addon = Services.GameGui.GetAddonByName<AtkUnitBase>(MenuAddonName, 1);
        if (addon is null || !addon->IsVisible)
            return [];

        var entries = new List<string>();
        foreach (ref var value in addon->AtkValuesSpan)
        {
            var type = value.Type & AtkValueType.TypeMask;
            if (type is not (AtkValueType.String or AtkValueType.ConstString) || !value.String.HasValue)
                continue;

            entries.Add(new ReadOnlySeStringSpan(value.String.Value).ExtractText());
        }

        return entries;
    }

    private static uint ResolveItemId(IMenuOpenedArgs args)
    {
        if (args.Target is MenuTargetInventory inv)
            return inv.TargetItem?.ItemId ?? 0;

        if (args.MenuType == ContextMenuType.Default
            && args.AddonName is { } addon
            && DefaultAddonAllowlist.Contains(addon))
        {
            return ReadItemDetailAgentItemId();
        }

        return 0;
    }

    private static unsafe uint ReadItemDetailAgentItemId()
    {
        var agent = AgentItemDetail.Instance();
        return agent is null ? 0 : agent->ItemId;
    }

    private static void Copy(string name)
    {
        ImGui.SetClipboardText(name);
        Services.Log.Debug($"Copied item name to clipboard: {name}");
    }

    public override void DrawConfig()
    {
        var cfg = Plugin.Config.Clipboard;
        var changed = false;

        ImGui.TextWrapped(
            "Skip adding the Clipboard entry to a menu that already contains one of the entries " +
            "ticked below. Entries other plugins add are listed here as they are seen, so a menu " +
            "shows both once before it is skipped.");
        ImGui.Separator();

        var options = cfg.LearnedEntries
            .Union(cfg.SuppressWhenPresent, StringComparer.OrdinalIgnoreCase)
            .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.Count == 0)
        {
            ImGui.TextDisabled("Nothing seen yet - open an item context menu once.");
        }
        else
        {
            ImGui.BeginChild("##ClipboardEntries", new Vector2(0, 140), true);
            for (var i = 0; i < options.Count; i++)
            {
                var entry = options[i];
                var ticked = cfg.SuppressWhenPresent.Contains(entry, StringComparer.OrdinalIgnoreCase);
                if (!ImGui.Checkbox($"{entry}##ClipboardEntry{i}", ref ticked))
                    continue;

                if (ticked)
                    cfg.SuppressWhenPresent.Add(entry);
                else
                    cfg.SuppressWhenPresent.RemoveAll(m => string.Equals(m, entry, StringComparison.OrdinalIgnoreCase));

                changed = true;
            }

            ImGui.EndChild();
        }

        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##ClipboardMatch", "Text to match...", ref _newMatch, 64);
        ImGui.SameLine();

        var match = _newMatch.Trim();
        ImGui.BeginDisabled(match.Length == 0);
        if (ImGui.Button("Add##Clipboard"))
        {
            if (!cfg.SuppressWhenPresent.Contains(match, StringComparer.OrdinalIgnoreCase))
                cfg.SuppressWhenPresent.Add(match);

            _newMatch = string.Empty;
            changed = true;
        }

        ImGui.EndDisabled();

        ImGui.BeginDisabled(cfg.LearnedEntries.Count == 0);
        if (ImGui.Button("Forget seen entries##Clipboard"))
        {
            cfg.LearnedEntries.Clear();
            changed = true;
        }

        ImGui.EndDisabled();

        ImGui.TextDisabled($"Currently skipping {cfg.SuppressedMenus.Count} menu(s).");

        if (!changed)
            return;

        // Every menu re-checks itself on its next open, so clearing here just stops a menu
        // staying skipped on a match that was only turned off.
        cfg.SuppressedMenus.Clear();
        Plugin.Config.Save();
    }
}
