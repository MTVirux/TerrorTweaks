using Dalamud.Plugin;

namespace TerrorTweaks;

public sealed class Plugin : IDalamudPlugin
{
    internal static Configuration Config { get; private set; } = null!;

    private readonly IDalamudPluginInterface _pluginInterface;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        pluginInterface.Create<Services>();
        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
    }

    public void Dispose()
    {
    }
}
