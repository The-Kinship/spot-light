using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace SpotLight;

public sealed class Plugin : IDalamudPlugin
{
    private static readonly string[] CommandNames = { "/spotlight", "/spot" };

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IDataManager GameData { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IGameInventory GameInventory { get; private set; } = null!;

    internal static Configuration Config { get; private set; } = null!;

    private readonly WindowSystem windows = new("SpotLight");
    private readonly MainWindow window;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        ItemNames.Load();

        window = new MainWindow();
        windows.AddWindow(window);

        foreach (var cmd in CommandNames)
            CommandManager.AddHandler(cmd, new CommandInfo(OnCommand) { HelpMessage = "Open or close Rafter Cat's Spot Light (your important items list)." });

        PluginInterface.UiBuilder.Draw += Highlighter.Draw;
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += Toggle;
        ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    /// <summary>Right-clicking an item in your inventory (or retainer, saddlebag, FC chest) adds a Spot Light option.</summary>
    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType != ContextMenuType.Inventory) return;
        if (args.Target is not MenuTargetInventory target || target.TargetItem is not { } item) return;
        var id = Configuration.BaseId(item.ItemId);
        if (id == 0) return;

        var important = Config.IsImportant(id);
        args.AddMenuItem(new MenuItem
        {
            Name = important ? "Spot Light: not important any more" : "Spot Light: mark as important",
            PrefixChar = 'S',
            PrefixColor = 500,
            OnClicked = _ =>
            {
                Config.SetImportant(id, !important);
                Chat.Print(important
                    ? $"[Spot Light] {ItemNames.Name(id)} is no longer marked important."
                    : $"[Spot Light] {ItemNames.Name(id)} is now marked important.");
            },
        });
    }

    private void OnCommand(string command, string args) => Toggle();

    private void Toggle() => window.Toggle();

    public void Dispose()
    {
        ContextMenu.OnMenuOpened -= OnMenuOpened;
        PluginInterface.UiBuilder.Draw -= Highlighter.Draw;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= Toggle;
        foreach (var cmd in CommandNames) CommandManager.RemoveHandler(cmd);
        windows.RemoveAllWindows();
    }
}
