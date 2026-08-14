using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using TerrorTweaks.Framework;
using TerrorTweaks.UI;

namespace TerrorTweaks;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/tterror";
    private const string CommandAlias = "/tt";

    internal static Configuration Config { get; private set; } = null!;

    private static ConfigWindow? _window;

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly WindowSystem _windowSystem = new("TerrorTweaks");
    private readonly TweakManager _tweakManager;
    private readonly ConfigWindow _configWindow;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        pluginInterface.Create<Services>();
        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        MenuPrefix.Register();

        _tweakManager = new TweakManager();
        _configWindow = new ConfigWindow(_tweakManager);
        _window = _configWindow;
        _windowSystem.AddWindow(_configWindow);

        Services.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the TerrorTweaks configuration window.",
        });

        Services.CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for " + CommandName + ".",
        });

        pluginInterface.UiBuilder.Draw         += _windowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        pluginInterface.UiBuilder.OpenMainUi   += OpenConfigUi;
    }

    private void OnCommand(string command, string args) => _configWindow.Toggle();

    private void OpenConfigUi() => _configWindow.IsOpen = true;

    // Lets a tweak's own window offer a way into the settings without reaching for the command.
    internal static void OpenConfig()
    {
        if (_window is not null)
            _window.IsOpen = true;
    }

    public void Dispose()
    {
        Services.CommandManager.RemoveHandler(CommandName);
        Services.CommandManager.RemoveHandler(CommandAlias);
        _pluginInterface.UiBuilder.Draw         -= _windowSystem.Draw;
        _pluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        _pluginInterface.UiBuilder.OpenMainUi   -= OpenConfigUi;
        _windowSystem.RemoveAllWindows();
        _window = null;
        _configWindow.Dispose();
        _tweakManager.Dispose();
        MenuPrefix.Unregister();
    }
}
