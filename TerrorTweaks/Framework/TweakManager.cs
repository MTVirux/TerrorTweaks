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

        foreach (var tweak in _tweaks.Where(t => Plugin.Config.EnabledTweaks.Contains(t.InternalName)))
            SafeEnable(tweak);
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
