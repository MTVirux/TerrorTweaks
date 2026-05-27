using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using TerrorTweaks.Framework;
using TerrorTweaks.Util;

namespace TerrorTweaks.Tweaks;

public sealed class JobRouletteTweak : Tweak
{
    private const string CommandName = "/jobrolo";

    public override string Name => "Job Roulette";

    public override string Description =>
        "Adds /jobrolo, which equips a random gearset chosen with equal weight per eligible job. " +
        "Optionally pass a role (e.g. /jobrolo tanks) to draw from just that category.";

    public override void Enable()
    {
        base.Enable();
        Services.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Equip a random gearset. Optionally pass a role, e.g. /jobrolo tanks.",
        });
    }

    public override void Disable()
    {
        Services.CommandManager.RemoveHandler(CommandName);
        base.Disable();
    }

    private void OnCommand(string command, string args) => Roll(args);

    private unsafe void Roll(string args)
    {
        var module = RaptureGearsetModule.Instance();
        if (module is null)
        {
            Services.Log.Debug("Job Roulette: gearset module unavailable.");
            return;
        }

        if (!TryBuildFilter(args, out var isEligible))
            return;

        var candidates = new List<(int slotIndex, uint classJobId)>();
        for (var i = 0; i < 100; i++)
        {
            if (!module->IsValidGearset(i))
                continue;

            var entry = module->GetGearset(i);
            if (entry is null)
                continue;

            uint classJobId = entry->ClassJob;
            if (isEligible(classJobId))
                candidates.Add((i, classJobId));
        }

        var chosen = GearsetRoulette.Pick(GearsetRoulette.FirstPerJob(candidates), Random.Shared);
        if (chosen is null)
        {
            Services.Chat.Print("Job Roulette: no matching gearsets.");
            return;
        }

        var slot = chosen.Value;
        var entryToEquip = module->GetGearset(slot);
        var gearsetName = entryToEquip is null ? $"Gearset {slot}" : entryToEquip->NameString;
        var jobAbbr = JobAbbreviation(entryToEquip is null ? 0u : entryToEquip->ClassJob);

        module->EquipGearset(slot, 0);
        Services.Chat.Print($"Job Roulette: {gearsetName} ({jobAbbr}).");
    }

    // Builds the per-job eligibility predicate. With an argument, restricts to that single
    // category (ignoring toggles); without one, applies the configured options. Returns
    // false (after reporting) when an argument is present but unrecognised.
    private static bool TryBuildFilter(string args, out Func<uint, bool> isEligible)
    {
        var trimmed = args.Trim();
        if (trimmed.Length > 0)
        {
            if (!JobCategoryParser.TryParse(trimmed, out var category))
            {
                Services.Chat.Print(
                    $"Job Roulette: unknown role '{trimmed}'. Try: tanks, healers, melee, casters, physranged, crafters, gatherers.");
                isEligible = _ => false;
                return false;
            }

            isEligible = jobId => JobClassifier.Classify(jobId) == category;
            return true;
        }

        var opts = ToOptions(Plugin.Config.JobRoulette);
        isEligible = jobId => GearsetRoulette.IsEligible(JobClassifier.Classify(jobId), IsLimitedJob(jobId), opts);
        return true;
    }

    private static RouletteOptions ToOptions(JobRouletteConfig cfg)
        => new(cfg.IncludeTanks, cfg.IncludeHealers, cfg.IncludeMelee, cfg.IncludePhysRanged,
            cfg.IncludeCasters, cfg.IncludeCrafters, cfg.IncludeGatherers, cfg.IncludeLimited);

    private static bool IsLimitedJob(uint classJobId)
        => Services.DataManager.GetExcelSheet<ClassJob>().TryGetRow(classJobId, out var row) && row.IsLimitedJob;

    private static string JobAbbreviation(uint classJobId)
        => Services.DataManager.GetExcelSheet<ClassJob>().TryGetRow(classJobId, out var row)
            ? row.Abbreviation.ExtractText()
            : classJobId.ToString();

    public override void DrawConfig()
    {
        var cfg = Plugin.Config.JobRoulette;
        var changed = false;

        ImGui.TextUnformatted("Combat roles");
        changed |= Checkbox("Tanks", cfg.IncludeTanks, v => cfg.IncludeTanks = v);
        changed |= Checkbox("Healers", cfg.IncludeHealers, v => cfg.IncludeHealers = v);
        changed |= Checkbox("Melee", cfg.IncludeMelee, v => cfg.IncludeMelee = v);
        changed |= Checkbox("Physical ranged", cfg.IncludePhysRanged, v => cfg.IncludePhysRanged = v);
        changed |= Checkbox("Casters", cfg.IncludeCasters, v => cfg.IncludeCasters = v);

        ImGui.Separator();
        changed |= Checkbox("Crafters", cfg.IncludeCrafters, v => cfg.IncludeCrafters = v);
        changed |= Checkbox("Gatherers", cfg.IncludeGatherers, v => cfg.IncludeGatherers = v);
        changed |= Checkbox("Limited jobs", cfg.IncludeLimited, v => cfg.IncludeLimited = v);

        if (changed)
            Plugin.Config.Save();
    }

    private static bool Checkbox(string label, bool value, Action<bool> set)
    {
        var v = value;
        if (!ImGui.Checkbox($"{label}##JobRoulette", ref v))
            return false;

        set(v);
        return true;
    }
}
