using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TerrorTweaks.Framework;

internal sealed class TweakManager : IDisposable
{
    private readonly List<Tweak> _tweaks = [];

    public IReadOnlyList<Tweak> Tweaks => _tweaks;

    public TweakManager()
    {
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes()
                     .Where(t => t.IsSubclassOf(typeof(Tweak)) && !t.IsAbstract))
        {
            try
            {
                _tweaks.Add((Tweak)Activator.CreateInstance(type)!);
            }
            catch (Exception ex)
            {
                Services.Log.Error(ex, $"Failed to instantiate tweak {type.Name}");
            }
        }

        ApplySavedOrder();

        foreach (var tweak in _tweaks.Where(t => Plugin.Config.EnabledTweaks.Contains(t.InternalName)))
            SafeEnable(tweak);
    }

    public bool IsNew(Tweak tweak) => !Plugin.Config.SeenTweaks.Contains(tweak.InternalName);

    public void MarkSeen(Tweak tweak)
    {
        if (Plugin.Config.SeenTweaks.Add(tweak.InternalName))
            Plugin.Config.Save();
    }

    public void MoveTweak(int from, int to)
    {
        TweakOrdering.Move(_tweaks, from, to);
        Plugin.Config.TweakOrder = [.. _tweaks.Select(t => t.InternalName)];
        Plugin.Config.Save();
    }

    private void ApplySavedOrder()
    {
        var config = Plugin.Config;
        var present = _tweaks.Select(t => t.InternalName).ToList();

        // No saved order means this config predates ordering, so everything here is already
        // familiar - marking it seen keeps an upgrade from lighting up every row as new.
        if (config.TweakOrder.Count == 0)
            config.SeenTweaks.UnionWith(present);

        var arranged = TweakOrdering.Arrange(config.TweakOrder, present);
        if (arranged.SequenceEqual(config.TweakOrder))
            return;

        var byName = new Dictionary<string, Tweak>(StringComparer.Ordinal);
        foreach (var tweak in _tweaks)
            byName[tweak.InternalName] = tweak;

        _tweaks.Clear();
        _tweaks.AddRange(arranged.Select(name => byName[name]));

        config.TweakOrder = arranged;
        config.Save();
    }

    public void SetEnabled(Tweak tweak, bool enabled)
    {
        if (enabled == tweak.Enabled)
            return;

        if (enabled)
            SafeEnable(tweak);
        else
            SafeDisable(tweak);

        // Persist the actual resulting state, not the requested one: if Enable/Disable
        // threw, the on-disk set must still match what the tweak is really doing.
        if (tweak.Enabled)
            Plugin.Config.EnabledTweaks.Add(tweak.InternalName);
        else
            Plugin.Config.EnabledTweaks.Remove(tweak.InternalName);

        Plugin.Config.Save();
    }

    private static void SafeEnable(Tweak tweak)
    {
        try
        {
            tweak.Enable();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, $"Failed to enable tweak {tweak.InternalName}");
        }
    }

    private static void SafeDisable(Tweak tweak)
    {
        try
        {
            tweak.Disable();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, $"Failed to disable tweak {tweak.InternalName}");
        }
    }

    public void Dispose()
    {
        // SafeDisable so one throwing tweak can't leave the rest with their hooks attached.
        foreach (var tweak in _tweaks.Where(t => t.Enabled))
            SafeDisable(tweak);
    }
}
