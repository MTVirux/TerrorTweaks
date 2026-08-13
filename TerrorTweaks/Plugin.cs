using System;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using TerrorTweaks.Framework;
using TerrorTweaks.UI;

namespace TerrorTweaks;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/tterror";
    private const string CommandAlias = "/tt";

    internal static Configuration Config { get; private set; } = null!;

    private readonly IDalamudPluginInterface _pluginInterface;
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

        Services.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the TerrorTweaks configuration window.",
        });

        Services.CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for " + CommandName + ".",
        });

        pluginInterface.UiBuilder.Draw         += _configWindow.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        pluginInterface.UiBuilder.OpenMainUi   += OpenConfigUi;
    }

    private void OnCommand(string command, string args) => _configWindow.Toggle();

    private void OpenConfigUi() => _configWindow.IsOpen = true;

    public void Dispose()
    {
        Services.CommandManager.RemoveHandler(CommandName);
        Services.CommandManager.RemoveHandler(CommandAlias);
        _pluginInterface.UiBuilder.Draw         -= _configWindow.Draw;
        _pluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        _pluginInterface.UiBuilder.OpenMainUi   -= OpenConfigUi;
        _configWindow.Dispose();
        _tweakManager.Dispose();
        MenuPrefix.Unregister();
    }
}
