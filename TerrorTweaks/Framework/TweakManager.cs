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
        {
            SafeEnable(tweak);
            Plugin.Config.EnabledTweaks.Add(tweak.InternalName);
        }
        else
        {
            tweak.Disable();
            Plugin.Config.EnabledTweaks.Remove(tweak.InternalName);
        }

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

    public void Dispose()
    {
        foreach (var tweak in _tweaks.Where(t => t.Enabled))
            tweak.Disable();
    }
}
