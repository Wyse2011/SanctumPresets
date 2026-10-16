using System;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SanctumPresets.Windows;

namespace SanctumPresets;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private const string Command = "/sanctum";

    public Configuration Configuration { get; }
    public Controller Controller { get; }
    public ColorService Colors { get; }

    public NoteEditorWindow NoteEditor { get; }
    public FileDialogManager FileDialog { get; } = new();

    private readonly WindowSystem windowSystem = new("SanctumPresets");
    private readonly MainWindow mainWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        var penumbra  = new PenumbraBridge(PluginInterface);
        Controller    = new Controller(penumbra, Configuration);
        Colors        = new ColorService(penumbra, Controller, Configuration,
            System.IO.Path.Combine(PluginInterface.GetPluginConfigDirectory(), "colors"));
        Controller.Colors = Colors;

        mainWindow = new MainWindow(this);
        NoteEditor = new NoteEditorWindow(this);
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(NoteEditor);

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Sanctum Presets. \"/sanctum <preset name>\" applies a preset, \"/sanctum reset\" clears every slot, \"/sanctum list\" lists presets, \"/sanctum note <text>\" adds a quick note.",
        });

        PluginInterface.UiBuilder.Draw         += windowSystem.Draw;
        PluginInterface.UiBuilder.Draw         += FileDialog.Draw;
        Framework.Update                       += OnFrameworkUpdate;

        // Colors live in a temporary Penumbra mod that disappears on restart; bring them back.
        if (Configuration.Color.Enabled)
            Colors.MarkDirty(3);
        PluginInterface.UiBuilder.OpenMainUi   += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
    }

    public void Dispose()
    {
        Framework.Update                       -= OnFrameworkUpdate;
        Colors.Dispose();
        PluginInterface.UiBuilder.Draw         -= windowSystem.Draw;
        PluginInterface.UiBuilder.Draw         -= FileDialog.Draw;
        PluginInterface.UiBuilder.OpenMainUi   -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        windowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(Command);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            Colors.Update();
        }
        catch (Exception e)
        {
            Log.Error(e, "Color update failed");
        }
    }

    public void ToggleMainUi()
        => mainWindow.Toggle();

    private void OnCommand(string command, string args)
    {
        args = args.Trim();
        if (args.Length == 0)
        {
            mainWindow.Toggle();
            return;
        }

        if (args.Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            Controller.ResetAll();
            Print();
            return;
        }

        if (args.StartsWith("note ", StringComparison.OrdinalIgnoreCase) || args.Equals("note", StringComparison.OrdinalIgnoreCase))
        {
            var text = args.Length > 4 ? args[5..].Trim() : string.Empty;
            if (text.Length == 0)
            {
                Chat.PrintError("[Sanctum] Usage: /sanctum note <text>   (links the effects in your current setup)");
                return;
            }

            var note = new Note { Kind = NoteKind.Fix, Text = text };
            foreach (var (slot, effect) in Controller.CurrentEffects())
                note.Effects.Add(new EffectLink { Effect = effect, Slot = slot });
            Configuration.Notes.Add(note);
            Configuration.Save();
            Chat.Print($"[Sanctum] Note saved, linked to {note.Effects.Count} effect(s) in your current setup. Edit it in /sanctum > Notes.");
            return;
        }

        if (args.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            Chat.Print(Configuration.Presets.Count == 0
                ? "[Sanctum] No presets saved yet. Open /sanctum to make one."
                : "[Sanctum] Presets: " + string.Join(", ", Configuration.Presets.Select(p => p.Name)));
            return;
        }

        var preset = Controller.FindPreset(args);
        if (preset == null)
        {
            Chat.PrintError($"[Sanctum] No preset named \"{args}\". Use /sanctum list to see them.");
            return;
        }

        Controller.Apply(preset);
        Print();
    }

    private void Print()
    {
        if (Controller.StatusIsError) Chat.PrintError("[Sanctum] " + Controller.Status);
        else Chat.Print("[Sanctum] " + Controller.Status);
    }
}
