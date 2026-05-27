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
        "Adds /jobrolo, which equips a random gearset chosen with equal weight per eligible job.";

    public override void Enable()
    {
        base.Enable();
        Services.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Equip a random gearset from your eligible jobs.",
        });
    }

    public override void Disable()
    {
        Services.CommandManager.RemoveHandler(CommandName);
        base.Disable();
    }

    private void OnCommand(string command, string args) => Roll();

    private unsafe void Roll()
    {
        var module = RaptureGearsetModule.Instance();
        if (module is null)
        {
            Services.Log.Debug("Job Roulette: gearset module unavailable.");
            return;
        }

        var opts = ToOptions(Plugin.Config.JobRoulette);
        var candidates = new List<(int slotIndex, uint classJobId)>();

        for (var i = 0; i < 100; i++)
        {
            if (!module->IsValidGearset(i))
                continue;

            var entry = module->GetGearset(i);
            if (entry is null)
                continue;

            uint classJobId = entry->ClassJob;
            if (GearsetRoulette.IsEligible(JobClassifier.Classify(classJobId), IsLimitedJob(classJobId), opts))
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

    private static RouletteOptions ToOptions(JobRouletteConfig cfg)
        => new(cfg.IncludeCrafters, cfg.IncludeGatherers, cfg.IncludeLimited);

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

        var crafters = cfg.IncludeCrafters;
        if (ImGui.Checkbox("Include crafters##JobRoulette", ref crafters))
        {
            cfg.IncludeCrafters = crafters;
            changed = true;
        }

        var gatherers = cfg.IncludeGatherers;
        if (ImGui.Checkbox("Include gatherers##JobRoulette", ref gatherers))
        {
            cfg.IncludeGatherers = gatherers;
            changed = true;
        }

        var limited = cfg.IncludeLimited;
        if (ImGui.Checkbox("Include limited jobs##JobRoulette", ref limited))
        {
            cfg.IncludeLimited = limited;
            changed = true;
        }

        if (changed)
            Plugin.Config.Save();
    }
}
