using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace SanctumPresets.Windows;

public sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    private Configuration Config => plugin.Configuration;
    private Controller Ctl => plugin.Controller;

    // Cached Penumbra state, refreshed about once a second or after actions.
    private DateTime nextRefresh = DateTime.MinValue;
    private bool available;
    private string unavailableReason = string.Empty;
    private (string Directory, string Name)? mod;
    private (Guid Id, string Name)? collection;
    private List<(string Group, string[] Options, int Type)> groups = new();
    private Dictionary<string, List<string>> current = new();
    private bool modEnabled;
    private List<(string Directory, string Name)> allMods = new();

    // UI state
    private string newPresetName = string.Empty;
    private string comboFilter = string.Empty;
    private int renaming = -1;
    private string renameBuffer = string.Empty;

    public MainWindow(Plugin plugin)
        : base("Sanctum Presets###SanctumPresetsMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void OnOpen()
        => nextRefresh = DateTime.MinValue;

    private void Refresh(bool force = false)
    {
        if (!force && DateTime.UtcNow < nextRefresh)
            return;

        nextRefresh = DateTime.UtcNow.AddSeconds(1);
        var pen = Ctl.Penumbra;
        available = pen.IsAvailable(out unavailableReason);
        if (!available)
            return;

        allMods    = pen.GetMods();
        mod        = Ctl.ResolveMod(allMods);
        collection = pen.GetPlayerCollection();
        groups     = mod != null ? Ctl.GetGroups(mod.Value.Directory) : new();
        current.Clear();
        modEnabled = false;
        if (mod != null && collection != null)
        {
            var cur = pen.GetCurrent(collection.Value.Id, mod.Value.Directory);
            if (cur != null)
            {
                modEnabled = cur.Value.Enabled;
                current    = cur.Value.Settings;
            }
        }
    }

    public override void Draw()
    {
        Refresh();

        if (!available)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, unavailableReason);
            ImGui.TextWrapped("Sanctum Presets needs Penumbra to be installed and enabled.");
            return;
        }

        DrawHeader();
        ImGui.Separator();
        if (mod == null || collection == null)
        {
            DrawSettings(defaultOpen: true);
            return;
        }

        var openNotes = Config.Notes.Count(n => !n.Done);
        using (var bar = ImRaii.TabBar("##tabs"))
        {
            if (bar)
            {
                using (var tab = ImRaii.TabItem("Setup & Presets"))
                {
                    if (tab)
                    {
                        DrawSlots();
                        DrawColorSummary();
                        ImGui.Separator();
                        DrawSavePreset();
                        DrawPresetList();
                        ImGui.Separator();
                        DrawSharing();
                        DrawSettings(defaultOpen: false);
                    }
                }

                using (var tab = ImRaii.TabItem($"Notes ({openNotes} open)###notes"))
                {
                    if (tab)
                        DrawNotesTab();
                }
            }
        }

        DrawStatus();
    }

    private void DrawHeader()
    {
        if (mod == null)
        {
            ImGui.TextColored(ImGuiColors.DalamudYellow, $"No mod matching \"{Config.AutoDetectName}\" found. Pick one under Settings below.");
            return;
        }

        ImGui.Text($"Mod: {mod.Value.Name}");
        if (collection != null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"   Collection: {collection.Value.Name}");
        }
        else
        {
            ImGui.TextColored(ImGuiColors.DalamudYellow, "Log in to a character to use presets.");
        }

        if (collection != null && !modEnabled)
            ImGui.TextColored(ImGuiColors.DalamudYellow, "The mod is disabled in this collection. Applying a preset will enable it.");
    }

    // ---------------- slots ----------------

    private void DrawSlots()
    {
        ImGui.Text("Current setup");
        using (var table = ImRaii.Table("##slots", 3, ImGuiTableFlags.SizingStretchProp))
        {
            if (table)
            {
                ImGui.TableSetupColumn("slot", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableSetupColumn("pick", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("tools", ImGuiTableColumnFlags.WidthFixed, 215);
                foreach (var g in groups)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(g.Group);
                    ImGui.TableNextColumn();
                    var sel = current.TryGetValue(g.Group, out var list) && list.Count > 0 ? string.Join(", ", list) : "---";
                    if (g.Type != 0)
                    {
                        ImGui.TextDisabled(sel + "  (multi-choice: change in Penumbra)");
                        continue;
                    }

                    if (SearchCombo("##" + g.Group, sel, g.Options, out var picked))
                    {
                        Ctl.SetSlot(g.Group, picked);
                        Refresh(true);
                    }

                    ImGui.TableNextColumn();
                    using (ImRaii.PushId(g.Group))
                    {
                        DrawSlotColorButton(g.Group, sel);
                        ImGui.SameLine();
                        DrawSlotNotes(g.Group, sel);
                    }
                }
            }
        }

        if (ImGui.Button("Reset all slots"))
        {
            Ctl.ResetAll();
            Refresh(true);
        }

        ImGui.SameLine();
        if (ImGui.Button("Redraw me"))
            Ctl.Penumbra.RedrawPlayer();

        ImGui.SameLine();
        var auto = Config.AutoRedraw;
        if (ImGui.Checkbox("Redraw automatically", ref auto))
        {
            Config.AutoRedraw = auto;
            Config.Save();
        }
    }

    private bool SearchCombo(string id, string preview, string[] options, out string picked)
    {
        picked = string.Empty;
        ImGui.SetNextItemWidth(-1);
        using var combo = ImRaii.Combo(id, preview, ImGuiComboFlags.HeightLargest);
        if (!combo)
            return false;

        if (ImGui.IsWindowAppearing())
        {
            comboFilter = string.Empty;
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filter", "Type to search...", ref comboFilter, 128);

        var changed = false;
        foreach (var o in options)
        {
            if (comboFilter.Length > 0 && !o.Contains(comboFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (ImGui.Selectable(o, o == preview))
            {
                picked  = o;
                changed = true;
            }
        }

        return changed;
    }

    // ---------------- color ----------------

    private static readonly (string Name, Vector3 Color)[] Swatches =
    {
        ("Blue",   new(0.25f, 0.55f, 1.00f)),
        ("Cyan",   new(0.20f, 0.95f, 1.00f)),
        ("Green",  new(0.30f, 1.00f, 0.35f)),
        ("Gold",   new(1.00f, 0.80f, 0.20f)),
        ("Red",    new(1.00f, 0.15f, 0.10f)),
        ("Pink",   new(1.00f, 0.40f, 0.80f)),
        ("Purple", new(0.65f, 0.30f, 1.00f)),
        ("White",  new(1.00f, 1.00f, 1.00f)),
    };

    private bool colorConfigDirty;
    private ColorSettings? copiedColor;

    private void ColorChanged(double delay = 0.25)
    {
        colorConfigDirty = true;
        plugin.Colors.MarkDirty(delay);
    }

    /// <summary> Before editing a single slot, turn a 1.2 "one color for everything" into per-slot colors. </summary>
    private void MigrateLegacy()
    {
        if (Config.Color.Enabled)
            Config.MigrateLegacyColor(groups.Where(g => g.Type == 0).Select(g => g.Group));
    }

    /// <summary> The "Customize" button on each slot row (tinted with the slot's color); opens that slot's adjuster. </summary>
    private void DrawSlotColorButton(string slot, string effect)
    {
        var c = Config.ColorFor(slot);
        var pushed = 0;
        if (c is { Enabled: true })
        {
            var col  = new Vector4(c.R, c.G, c.B, 1f);
            var luma = 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
            ImGui.PushStyleColor(ImGuiCol.Button, col);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, col * new Vector4(0.85f, 0.85f, 0.85f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, col * new Vector4(0.7f, 0.7f, 0.7f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Text, luma > 0.6f ? new Vector4(0, 0, 0, 1) : new Vector4(1, 1, 1, 1));
            pushed = 4;
        }

        var label = (c is { Chroma: true } ? "Chroma" : "Customize") + (c is { SpeedChanged: true } ? FormattableString.Invariant($" {c.Speed:0.##}x") : string.Empty);
        var clicked = ImGui.Button(label + "##customize", new Vector2(110, 0));
        if (pushed > 0)
            ImGui.PopStyleColor(pushed);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(c != null ? $"{slot}: {c.Describe()}\nClick to change." : $"{slot} plays normally. Click to change its color or speed.");

        if (clicked)
        {
            MigrateLegacy();
            ImGui.OpenPopup("slotcolorpopup");
        }

        using var popup = ImRaii.Popup("slotcolorpopup");
        if (popup)
            DrawSlotColorEditor(slot, effect);
    }

    private void DrawSlotColorEditor(string slot, string effect)
    {
        var c = Config.SlotColor(slot);

        ImGui.Text(slot);
        ImGui.SameLine();
        ImGui.TextDisabled(effect.Trim() == "---" ? "(no effect chosen)" : effect);

        ImGui.Separator();
        ImGui.Text("Color");
        var on = c.Enabled;
        if (ImGui.Checkbox("Color this slot", ref on))
        {
            c.Enabled = on;
            ColorChanged(0);
        }

        if (!c.Enabled)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(original colors)");
        }

        // Picking a color, swatch or slider turns the slot on: that's clearly what the user wants.
        var col = new Vector3(c.R, c.G, c.B);
        ImGui.SetNextItemWidth(200);
        if (ImGui.ColorPicker3("##wheel", ref col,
                ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.DisplayHex | ImGuiColorEditFlags.NoAlpha))
        {
            (c.R, c.G, c.B) = (col.X, col.Y, col.Z);
            c.Enabled = true;
            ColorChanged();
        }

        ImGui.SameLine();
        ImGui.BeginGroup();

        for (var i = 0; i < Swatches.Length; i++)
        {
            var (name, sw) = Swatches[i];
            if (i > 0 && i % 4 != 0) ImGui.SameLine();
            if (ImGui.ColorButton($"{name}##sw{i}", new Vector4(sw, 1f), ImGuiColorEditFlags.NoAlpha, new Vector2(28, 28)))
            {
                (c.R, c.G, c.B) = (sw.X, sw.Y, sw.Z);
                c.Enabled = true;
                ColorChanged(0);
            }
        }

        ImGui.Spacing();
        if (ImGui.RadioButton("Recolor", c.Mode == ColorMode.Recolor)) { c.Mode = ColorMode.Recolor; c.Enabled = true; ColorChanged(0); }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Shifts every colored part of the effect to this color. White-hot parts stay white.");
        ImGui.SameLine();
        if (ImGui.RadioButton("Tint", c.Mode == ColorMode.Tint)) { c.Mode = ColorMode.Tint; c.Enabled = true; ColorChanged(0); }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Multiplies the whole effect by this color, like a colored filter.");

        var strength = c.Strength * 100f;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderFloat("Strength", ref strength, 0f, 100f, "%.0f%%", ImGuiSliderFlags.AlwaysClamp))
        {
            c.Strength = strength / 100f;
            c.Enabled  = true;
            ColorChanged();
        }

        var bright = c.Brightness * 100f;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderFloat("Brightness", ref bright, 20f, 300f, "%.0f%%", ImGuiSliderFlags.AlwaysClamp))
        {
            c.Brightness = bright / 100f;
            c.Enabled    = true;
            ColorChanged();
        }

        ImGui.EndGroup();

        // ---- Chroma ----
        ImGui.Separator();
        var chroma = c.Chroma;
        if (ImGui.Checkbox("Chroma (cycle through every color)", ref chroma))
        {
            c.Chroma = chroma;
            ColorChanged(0);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The color cycle is written into the effect, so the game animates it by itself\nand everyone who sees you (through Lightless) sees it cycle too.");

        using (ImRaii.Disabled(!c.Chroma))
        {
            var secs = c.ChromaSeconds;
            ImGui.SetNextItemWidth(120);
            ImGui.InputFloat("seconds per cycle##chromasecs", ref secs, 0f, 0f, "%.2f");
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                c.ChromaSeconds = Math.Clamp(secs, AvfxChroma.MinSeconds, AvfxChroma.MaxSeconds);
                ColorChanged(0);
            }
            else if (ImGui.IsItemActive())
            {
                c.ChromaSeconds = Math.Clamp(secs, AvfxChroma.MinSeconds, AvfxChroma.MaxSeconds);
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(FormattableString.Invariant($"Type any time from {AvfxChroma.MinSeconds:0.#} to {AvfxChroma.MaxSeconds:0} seconds, then press Enter."));

            foreach (var (label, value) in new[] { ("2s", 2f), ("5s", 5f), ("10s", 10f), ("30s", 30f), ("60s", 60f) })
            {
                ImGui.SameLine();
                if (ImGui.SmallButton(label + "##chroma"))
                {
                    c.ChromaSeconds = value;
                    ColorChanged(0);
                }
            }

            var cs = c.ChromaStrength * 100f;
            ImGui.SetNextItemWidth(260);
            if (ImGui.SliderFloat("Chroma strength", ref cs, 0f, 100f, "%.0f%%", ImGuiSliderFlags.AlwaysClamp))
            {
                c.ChromaStrength = cs / 100f;
                ColorChanged(0.4);
            }

            ImGui.TextDisabled("Each viewer's cycle starts when their game loads your effect,\nso two people may see different colors at the same moment.");
        }

        // ---- Size & position ----
        ImGui.Separator();
        ImGui.Text("Size & position");
        ImGui.SameLine();
        ImGui.TextDisabled("(Ctrl+click a slider to type a number)");

        var sizePct = c.Size * 100f;
        ImGui.SetNextItemWidth(260);
        if (ImGui.SliderFloat("Size##xf", ref sizePct, AvfxTransform.MinSize * 100f, AvfxTransform.MaxSize * 100f, "%.0f%%", ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.Logarithmic))
        {
            c.Size = MathF.Round(sizePct) / 100f;
            ColorChanged(0.4);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Make the whole effect bigger or smaller. For skies, bigger also pushes the sky further back.");

        var height = c.Height;
        ImGui.SetNextItemWidth(260);
        if (ImGui.SliderFloat("Height##xf", ref height, -50f, 50f, "%.2f yalms"))
        {
            c.Height = height;
            ColorChanged(0.4);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Raise (+) or lower (-) the effect. Ctrl+click to type values beyond the slider.");

        var forward = c.Forward;
        ImGui.SetNextItemWidth(260);
        if (ImGui.SliderFloat("Forward / back##xf", ref forward, -100f, 100f, "%.2f yalms"))
        {
            c.Forward = forward;
            ColorChanged(0.4);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Move the effect forward (+) or back (-). Ctrl+click to type values beyond the slider.");

        if (ImGui.SmallButton("Reset size & position"))
        {
            c.Size = 1f; c.Height = 0f; c.Forward = 0f;
            ColorChanged(0);
        }

        // ---- Speed ----
        ImGui.Separator();
        ImGui.Text("Speed");
        ImGui.SameLine();
        ImGui.TextDisabled(c.SpeedChanged ? FormattableString.Invariant($"({c.Speed * 100:0}% of normal)") : "(normal)");

        var speedPct = c.Speed * 100f;
        ImGui.SetNextItemWidth(260);
        if (ImGui.SliderFloat("##speed", ref speedPct, AvfxSpeed.MinSpeed * 100f, AvfxSpeed.MaxSpeed * 100f, "%.0f%%",
                ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.Logarithmic))
        {
            c.Speed = MathF.Round(speedPct) / 100f;
            ColorChanged(0.4);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("How fast the effect plays. Ctrl+click to type an exact value.");

        foreach (var (label, value) in new[] { ("0.5x", 0.5f), ("0.75x", 0.75f), ("Normal", 1f), ("1.5x", 1.5f), ("2x", 2f), ("3x", 3f) })
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(label))
            {
                c.Speed = value;
                ColorChanged(0);
            }
        }

        ImGui.Separator();
        ImGui.BeginGroup();
        if (ImGui.Button("Same on all slots"))
        {
            foreach (var g in groups.Where(g => g.Type == 0 && g.Group != slot))
            {
                Config.SlotColors[g.Group] = c.Clone();
            }

            ColorChanged(0);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Give every slot this color and speed.");

        if (ImGui.Button("Copy"))
            copiedColor = c.Clone();
        ImGui.SameLine();
        using (ImRaii.Disabled(copiedColor == null))
        {
            if (ImGui.Button("Paste") && copiedColor != null)
            {
                Config.SlotColors[slot] = copiedColor.Clone();
                ColorChanged(0);
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(copiedColor == null ? "Copy a color from any slot first." : "Paste " + copiedColor.Describe());

        ImGui.SameLine();
        if (ImGui.Button("Original colors"))
        {
            c.Enabled = false;
            ColorChanged(0);
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset slot"))
        {
            Config.SlotColors[slot] = new ColorSettings();
            ColorChanged(0);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Original colors, no chroma, normal size, position and speed.");

        ImGui.EndGroup();
        SaveColorsWhenReleased();
    }

    /// <summary> One line under the slots: how many are colored, the last result, and a clear button. </summary>
    private void DrawColorSummary()
    {
        var colored = groups.Count(g => g.Type == 0 && Config.ColorFor(g.Group) != null);
        var colors  = plugin.Colors;
        if (colored == 0)
        {
            ImGui.TextDisabled("All slots play normally. Click \"Customize\" on a slot to change its color or speed.");
        }
        else
        {
            ImGui.Text($"{colored} slot{(colored == 1 ? "" : "s")} customized.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Reset all customizing"))
                Ctl.ClearColors();
        }

        if (colors.LastResult.Length > 0 && (colored > 0 || colors.LastResultIsError))
        {
            if (colors.LastResultIsError) ImGui.TextColored(ImGuiColors.DalamudRed, colors.LastResult);
            else ImGui.TextDisabled(colors.LastResult);
        }

        SaveColorsWhenReleased();
    }

    /// <summary> Save once the mouse is released, not on every frame of a drag. </summary>
    private void SaveColorsWhenReleased()
    {
        if (colorConfigDirty && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            colorConfigDirty = false;
            Config.Save();
        }
    }

    // ---------------- presets ----------------

    private void DrawSavePreset()
    {
        ImGui.Text("Presets");
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 170);
        ImGui.InputTextWithHint("##newname", "Name for the current setup...", ref newPresetName, 64);
        ImGui.SameLine();
        var name     = newPresetName.Trim();
        var existing = name.Length > 0 ? Ctl.FindPreset(name) : null;
        using (ImRaii.Disabled(name.Length == 0))
        {
            if (ImGui.Button(existing == null ? "Save as new preset" : "Overwrite preset", new Vector2(160, 0)))
            {
                var captured = Ctl.Capture();
                if (captured != null)
                {
                    if (existing != null)
                    {
                        existing.Settings = captured;
                        existing.SlotColors = Ctl.CaptureColors();
                        existing.Color      = null;
                        existing.Saved    = DateTime.Now;
                    }
                    else
                    {
                        Config.Presets.Add(new Preset { Name = name, Settings = captured, SlotColors = Ctl.CaptureColors() });
                    }

                    Config.Save();
                    Ctl.Report($"Saved \"{name}\".");
                    newPresetName = string.Empty;
                }
            }
        }
    }

    private void DrawPresetList()
    {
        if (Config.Presets.Count == 0)
        {
            ImGui.TextDisabled("No presets yet. Set up the slots above, type a name, and press Save.");
            return;
        }

        using var table = ImRaii.Table("##presets", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("apply", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("tools", ImGuiTableColumnFlags.WidthFixed, 300);

        int? delete = null, moveUp = null, moveDown = null;
        for (var i = 0; i < Config.Presets.Count; i++)
        {
            var p = Config.Presets[i];
            using var id = ImRaii.PushId(i);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            if (ImGui.Button("Apply"))
            {
                Ctl.Apply(p);
                Refresh(true);
            }

            ImGui.TableNextColumn();
            if (renaming == i)
            {
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputText("##rename", ref renameBuffer, 64, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    var newName = renameBuffer.Trim();
                    if (newName.Length > 0 && (Ctl.FindPreset(newName) is null || Ctl.FindPreset(newName) == p))
                    {
                        p.Name = newName;
                        Config.Save();
                    }
                    else
                    {
                        Ctl.Report($"Can't rename: \"{newName}\" is empty or already used.", true);
                    }

                    renaming = -1;
                }
            }
            else
            {
                ImGui.Text(p.Name);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(Summary(p));
                var linked = NoteTools.ForPreset(Config.Notes, p.Id);
                var open   = linked.Count(n => !n.Done);
                if (linked.Count > 0)
                {
                    ImGui.SameLine();
                    if (open > 0) ImGui.TextColored(ImGuiColors.DalamudYellow, $"({open} note{(open == 1 ? "" : "s")})");
                    else ImGui.TextDisabled("(notes done)");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(NotesTooltip(linked));
                }
            }

            ImGui.TableNextColumn();
            if (ImGui.Button("Update"))
            {
                var captured = Ctl.Capture();
                if (captured != null)
                {
                    p.Settings = captured;
                    p.SlotColors = Ctl.CaptureColors();
                    p.Color      = null;
                    p.Saved    = DateTime.Now;
                    Config.Save();
                    Ctl.Report($"Updated \"{p.Name}\" with the current setup.");
                }
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Replace this preset with the current setup.");

            ImGui.SameLine();
            if (ImGui.Button("Note"))
                plugin.NoteEditor.OpenNew(NoteKind.Fix, preset: p.Id);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Write a note linked to this preset.");

            ImGui.SameLine();
            if (ImGui.Button(renaming == i ? "Cancel" : "Rename"))
            {
                if (renaming == i) renaming = -1;
                else { renaming = i; renameBuffer = p.Name; }
            }

            ImGui.SameLine();
            if (ImGui.Button("^") && i > 0) moveUp = i;
            ImGui.SameLine();
            if (ImGui.Button("v") && i < Config.Presets.Count - 1) moveDown = i;
            ImGui.SameLine();
            if (ImGui.Button("Copy"))
            {
                ImGui.SetClipboardText(Controller.Export(new[] { p }));
                Ctl.Report($"Copied \"{p.Name}\" to the clipboard.");
            }

            ImGui.SameLine();
            var ctrl = ImGui.GetIO().KeyCtrl;
            using (ImRaii.Disabled(!ctrl))
            {
                if (ImGui.Button("X"))
                    delete = i;
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Hold Ctrl and click to delete.");
        }

        if (delete is { } d)
        {
            Ctl.DeletePreset(Config.Presets[d]);
            renaming = -1;
        }
        else if (moveUp is { } u)
        {
            (Config.Presets[u - 1], Config.Presets[u]) = (Config.Presets[u], Config.Presets[u - 1]);
            renaming = -1;
            Config.Save();
        }
        else if (moveDown is { } dn)
        {
            (Config.Presets[dn + 1], Config.Presets[dn]) = (Config.Presets[dn], Config.Presets[dn + 1]);
            renaming = -1;
            Config.Save();
        }
    }

    private static string Summary(Preset p)
    {
        var lines = p.Settings.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}: {(kv.Value.Count == 0 ? "---" : string.Join(", ", kv.Value))}");
        var color = "\n" + string.Join("\n", p.DescribeColors().Select(l => "Color: " + l));
        return string.Join("\n", lines) + color + $"\n\nSaved {p.Saved:g}.  Chat: /sanctum {p.Name}";
    }

    // ---------------- notes ----------------

    private string notesSearch = string.Empty;
    private int notesKindFilter = -1; // -1 = all
    private bool notesShowDone;

    private void DrawSlotNotes(string slot, string effect)
    {
        var hasEffect = effect.Trim() != "---";
        var open = hasEffect ? NoteTools.OpenFor(Config.Notes, effect, slot) : new List<Note>();
        using (ImRaii.Disabled(!hasEffect))
        {
            if (ImGui.Button("+ Note##" + slot))
                plugin.NoteEditor.OpenNew(NoteKind.Fix, new EffectLink { Effect = effect, Slot = slot });
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(hasEffect ? $"Write a note about \"{effect}\" in {slot}." : "Pick an effect first.");

        if (open.Count > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudYellow, $"! {open.Count}");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(NotesTooltip(open));
        }
    }

    private static string NotesTooltip(IEnumerable<Note> notes)
        => string.Join("\n", notes.Select(n => $"{(n.Done ? "[x]" : "[ ]")} {Note.KindLabel(n.Kind)}: {n.Title}"));

    private void DrawNotesTab()
    {
        if (ImGui.Button("New note"))
            plugin.NoteEditor.OpenNew(NoteKind.Fix);
        ImGui.SameLine();
        if (ImGui.Button("New idea"))
            plugin.NoteEditor.OpenNew(NoteKind.Idea);

        ImGui.SameLine();
        if (ImGui.Button("Export to .txt..."))
        {
            var start = Config.LastExportFolder.Length > 0 && System.IO.Directory.Exists(Config.LastExportFolder)
                ? Config.LastExportFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            plugin.FileDialog.SaveFileDialog("Export Sanctum notes", ".txt", $"Sanctum notes {DateTime.Now:yyyy-MM-dd}", ".txt",
                (ok, path) =>
                {
                    if (ok && !string.IsNullOrWhiteSpace(path))
                        Ctl.ExportNotes(path, mod?.Name ?? string.Empty);
                }, start, true);
        }

        ImGui.SameLine();
        if (ImGui.Button("Copy as text"))
        {
            ImGui.SetClipboardText(NoteTools.BuildReport(Config, mod?.Name ?? string.Empty, DateTime.Now));
            Ctl.Report("Notes copied to the clipboard as text.");
        }

        // Filters
        ImGui.SetNextItemWidth(160);
        var kinds = new[] { "All types", Note.KindLabel(NoteKind.Fix), Note.KindLabel(NoteKind.Idea), Note.KindLabel(NoteKind.General) };
        using (var combo = ImRaii.Combo("##kind", kinds[notesKindFilter + 1]))
        {
            if (combo)
                for (var i = 0; i < kinds.Length; i++)
                    if (ImGui.Selectable(kinds[i], notesKindFilter == i - 1))
                        notesKindFilter = i - 1;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 110);
        ImGui.InputTextWithHint("##notesearch", "Search notes, effects, presets...", ref notesSearch, 128);
        ImGui.SameLine();
        ImGui.Checkbox("Show done", ref notesShowDone);

        var presetNames = Config.Presets.ToDictionary(p => p.Id, p => p.Name);
        var list = Config.Notes
            .Where(n => notesShowDone || !n.Done)
            .Where(n => notesKindFilter < 0 || (int)n.Kind == notesKindFilter)
            .Where(n => notesSearch.Length == 0
                     || n.Text.Contains(notesSearch, StringComparison.OrdinalIgnoreCase)
                     || n.Effects.Any(e => e.Effect.Contains(notesSearch, StringComparison.OrdinalIgnoreCase))
                     || n.Presets.Any(pid => presetNames.TryGetValue(pid, out var nm) && nm.Contains(notesSearch, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(n => n.Done).ThenBy(n => n.Kind).ThenByDescending(n => n.Updated)
            .ToList();

        ImGui.Separator();
        if (Config.Notes.Count == 0)
        {
            ImGui.TextWrapped("No notes yet. Use \"+ Note\" next to a slot or preset, the buttons above, or type /sanctum note <text> in chat.");
            return;
        }

        if (list.Count == 0)
        {
            ImGui.TextDisabled("No notes match the filters.");
            return;
        }

        using var child = ImRaii.Child("##notes", new Vector2(0, -ImGui.GetFrameHeightWithSpacing()), false);
        if (!child)
            return;

        Note? delete = null;
        foreach (var n in list)
        {
            using var id = ImRaii.PushId(n.Id.ToString());
            var done = n.Done;
            if (ImGui.Checkbox("##done", ref done))
            {
                n.Done    = done;
                n.Updated = DateTime.Now;
                Config.Save();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(done ? "Mark as not done" : "Mark as done");

            ImGui.SameLine();
            var color = n.Kind switch
            {
                NoteKind.Fix  => ImGuiColors.DalamudRed,
                NoteKind.Idea => ImGuiColors.HealerGreen,
                _             => ImGuiColors.DalamudGrey,
            };
            if (n.Done) ImGui.TextDisabled($"[{Note.KindLabel(n.Kind)}]");
            else ImGui.TextColored(color, $"[{Note.KindLabel(n.Kind)}]");

            ImGui.SameLine();
            if (n.Done) ImGui.TextDisabled(n.Title);
            else ImGui.Text(n.Title);

            ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - 95);
            if (ImGui.SmallButton("Edit"))
                plugin.NoteEditor.OpenEdit(n);
            ImGui.SameLine();
            using (ImRaii.Disabled(!ImGui.GetIO().KeyCtrl))
            {
                if (ImGui.SmallButton("X"))
                    delete = n;
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Hold Ctrl and click to delete.");

            using (ImRaii.PushIndent(30f))
            {
                var rest = n.Text.Replace("\r", string.Empty).Trim();
                var nl = rest.IndexOf('\n');
                if (nl >= 0)
                    ImGui.TextWrapped(rest[(nl + 1)..]);
                foreach (var e in n.Effects)
                    ImGui.TextDisabled("Effect: " + e);
                foreach (var pid in n.Presets)
                    ImGui.TextDisabled("Preset: " + (presetNames.TryGetValue(pid, out var nm) ? nm : "(deleted preset)"));
            }

            ImGui.Spacing();
        }

        if (delete != null)
        {
            Config.Notes.Remove(delete);
            Config.Save();
            Ctl.Report("Note deleted.");
        }
    }

    // ---------------- sharing ----------------

    private void DrawSharing()
    {
        using (ImRaii.Disabled(Config.Presets.Count == 0))
        {
            if (ImGui.Button("Copy all presets to clipboard"))
            {
                ImGui.SetClipboardText(Controller.Export(Config.Presets));
                Ctl.Report($"Copied {Config.Presets.Count} preset(s). Paste the text anywhere to share.");
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Import from clipboard"))
        {
            var imported = Controller.Import(ImGui.GetClipboardText() ?? string.Empty);
            if (imported == null || imported.Count == 0)
            {
                Ctl.Report("The clipboard doesn't contain Sanctum presets (they start with \"SANCTUM1:\").", true);
            }
            else
            {
                int added = 0, replaced = 0;
                foreach (var p in imported)
                {
                    var existing = Ctl.FindPreset(p.Name);
                    if (existing != null) { existing.Settings = p.Settings; existing.Color = p.Color; existing.SlotColors = p.SlotColors; existing.Saved = DateTime.Now; replaced++; }
                    else { Config.Presets.Add(p); added++; }
                }

                Config.Save();
                Ctl.ReportImport(added, replaced);
            }
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Presets with the same name are replaced.");
    }

    // ---------------- settings ----------------

    private void DrawSettings(bool defaultOpen)
    {
        if (!ImGui.CollapsingHeader("Settings", defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None))
            return;

        var showAll = Config.ShowAllMods;
        if (ImGui.Checkbox("Show all mods", ref showAll))
        {
            Config.ShowAllMods = showAll;
            Config.Save();
        }

        var candidates = Config.ShowAllMods
            ? allMods
            : allMods.Where(m => m.Name.Contains("Sanctum", StringComparison.OrdinalIgnoreCase)
                              || m.Name.Contains(Config.AutoDetectName, StringComparison.OrdinalIgnoreCase)).ToList();

        var preview = mod?.Name ?? "(none)";
        ImGui.SetNextItemWidth(-1);
        using (var combo = ImRaii.Combo("##modpick", "Mod: " + preview))
        {
            if (combo)
            {
                if (ImGui.Selectable("Auto-detect", Config.ModDirectory.Length == 0))
                {
                    Config.ModDirectory = string.Empty;
                    Config.Save();
                    Refresh(true);
                }

                foreach (var m in candidates)
                {
                    if (ImGui.Selectable($"{m.Name}##{m.Directory}", m.Directory == Config.ModDirectory))
                    {
                        Config.ModDirectory = m.Directory;
                        Config.Save();
                        Refresh(true);
                    }
                }
            }
        }

        ImGui.TextDisabled("Presets store slot names and choices, so they keep working when the mod is updated,\nas long as the slot and effect names stay the same.");
    }

    private void DrawStatus()
    {
        if (Ctl.Status.Length == 0)
            return;

        ImGui.Separator();
        if (Ctl.StatusIsError) ImGui.TextColored(ImGuiColors.DalamudRed, Ctl.Status);
        else ImGui.TextColored(ImGuiColors.HealerGreen, Ctl.Status);
    }
}
