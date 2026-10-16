using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace SanctumPresets.Windows;

public sealed class NoteEditorWindow : Window
{
    private readonly Plugin plugin;
    private Configuration Config => plugin.Configuration;

    private Note draft = new();
    private bool isNew;

    // Choices for linking, captured when the editor opens.
    private List<string> slots = new();
    private List<string> effects = new();
    private HashSet<string> existing = new();

    private string effectFilter = string.Empty;
    private string pickedEffect = string.Empty;
    private int pickedSlot; // 0 = any slot
    private string presetFilter = string.Empty;

    public NoteEditorWindow(Plugin plugin)
        : base("Note###SanctumNoteEditor")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    /// <summary> Open the editor for a new note, optionally pre-linked to an effect and/or preset. </summary>
    public void OpenNew(NoteKind kind, EffectLink? effect = null, Guid? preset = null)
    {
        draft = new Note { Kind = kind };
        if (effect != null) draft.Effects.Add(effect);
        if (preset != null) draft.Presets.Add(preset.Value);
        isNew = true;
        Prepare();
    }

    /// <summary> Open the editor on an existing note (edits a copy until Save). </summary>
    public void OpenEdit(Note note)
    {
        draft = note.Clone();
        isNew = false;
        Prepare();
    }

    private void Prepare()
    {
        var mod = plugin.Controller.ResolveMod();
        var groups = mod != null ? plugin.Controller.GetGroups(mod.Value.Directory) : new();
        slots = groups.Select(g => g.Group).ToList();
        effects = groups.SelectMany(g => g.Options).Where(o => o.Trim() != "---").Distinct().OrderBy(o => o, StringComparer.OrdinalIgnoreCase).ToList();
        existing = effects.ToHashSet();
        // Keep effects that are linked but no longer in the mod, so they stay visible.
        foreach (var l in draft.Effects.Where(l => !effects.Contains(l.Effect)))
            effects.Add(l.Effect);

        effectFilter = presetFilter = pickedEffect = string.Empty;
        pickedSlot   = 0;
        WindowName   = (isNew ? "New note" : "Edit note") + "###SanctumNoteEditor";
        IsOpen       = true;
        BringToFront();
    }

    public override void Draw()
    {
        // Type
        ImGui.Text("Type:");
        foreach (NoteKind k in Enum.GetValues<NoteKind>())
        {
            ImGui.SameLine();
            if (ImGui.RadioButton(Note.KindLabel(k), draft.Kind == k))
                draft.Kind = k;
        }

        ImGui.SameLine(0, 30);
        var done = draft.Done;
        if (ImGui.Checkbox("Done", ref done))
            draft.Done = done;

        // Text
        var text = draft.Text;
        if (ImGui.InputTextMultiline("##text", ref text, 4000, new Vector2(-1, 110)))
            draft.Text = text;
        if (draft.Text.Length == 0)
            ImGui.TextDisabled("e.g. \"Mist needs to be lowered about 1 unit in the ring slot\" or \"Idea: add a snow layer to Molten Night\"");

        ImGui.Separator();
        DrawEffectLinks();
        ImGui.Separator();
        DrawPresetLinks();
        ImGui.Separator();

        // Buttons
        using (ImRaii.Disabled(draft.Text.Trim().Length == 0))
        {
            if (ImGui.Button("Save note", new Vector2(120, 0)))
                Save();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 0)))
            IsOpen = false;

        if (!isNew)
        {
            ImGui.SameLine();
            using (ImRaii.Disabled(!ImGui.GetIO().KeyCtrl))
            {
                if (ImGui.Button("Delete note", new Vector2(120, 0)))
                {
                    Config.Notes.RemoveAll(n => n.Id == draft.Id);
                    Config.Save();
                    plugin.Controller.Report("Note deleted.");
                    IsOpen = false;
                }
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Hold Ctrl and click to delete.");
        }
    }

    private void DrawEffectLinks()
    {
        ImGui.Text("Linked effects");
        if (draft.Effects.Count == 0)
            ImGui.TextDisabled("  none");

        EffectLink? remove = null;
        foreach (var l in draft.Effects)
        {
            using var id = ImRaii.PushId(l.ToString());
            if (ImGui.SmallButton("x")) remove = l;
            ImGui.SameLine();
            if (existing.Count > 0 && !existing.Contains(l.Effect))
                ImGui.TextColored(ImGuiColors.DalamudYellow, l + "  (not in the mod anymore)");
            else
                ImGui.Text(l.ToString());
        }

        if (remove != null)
            draft.Effects.Remove(remove);

        // Add an effect link
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.6f);
        using (var combo = ImRaii.Combo("##addeffect", pickedEffect.Length > 0 ? pickedEffect : "Pick an effect to link...", ImGuiComboFlags.HeightLargest))
        {
            if (combo)
            {
                if (ImGui.IsWindowAppearing())
                {
                    effectFilter = string.Empty;
                    ImGui.SetKeyboardFocusHere();
                }

                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##ef", "Type to search...", ref effectFilter, 128);
                foreach (var e in effects)
                {
                    if (effectFilter.Length > 0 && !e.Contains(effectFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (ImGui.Selectable(e, e == pickedEffect))
                        pickedEffect = e;
                }
            }
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 70);
        var slotNames = new List<string> { "Any slot" };
        slotNames.AddRange(slots);
        using (var combo = ImRaii.Combo("##addslot", slotNames[Math.Clamp(pickedSlot, 0, slotNames.Count - 1)]))
        {
            if (combo)
                for (var i = 0; i < slotNames.Count; i++)
                    if (ImGui.Selectable(slotNames[i], i == pickedSlot))
                        pickedSlot = i;
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(pickedEffect.Length == 0))
        {
            if (ImGui.Button("Link##effect"))
            {
                var link = new EffectLink { Effect = pickedEffect, Slot = pickedSlot == 0 ? string.Empty : slots[pickedSlot - 1] };
                if (!draft.Effects.Any(x => x.Effect == link.Effect && x.Slot == link.Slot))
                    draft.Effects.Add(link);
                pickedEffect = string.Empty;
            }
        }

        if (ImGui.Button("Link everything in the current setup"))
        {
            foreach (var (slot, effect) in plugin.Controller.CurrentEffects())
                if (!draft.Effects.Any(x => x.Effect == effect && x.Slot == slot))
                    draft.Effects.Add(new EffectLink { Effect = effect, Slot = slot });
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Adds the effect currently chosen in each slot (skips empty slots).");
    }

    private void DrawPresetLinks()
    {
        ImGui.Text("Linked presets");
        if (draft.Presets.Count == 0)
            ImGui.TextDisabled("  none");

        Guid? remove = null;
        foreach (var pid in draft.Presets)
        {
            using var id = ImRaii.PushId(pid.ToString());
            if (ImGui.SmallButton("x")) remove = pid;
            ImGui.SameLine();
            var p = Config.Presets.FirstOrDefault(x => x.Id == pid);
            if (p != null) ImGui.Text(p.Name);
            else ImGui.TextColored(ImGuiColors.DalamudYellow, "(deleted preset)");
        }

        if (remove != null)
            draft.Presets.Remove(remove.Value);

        var available = Config.Presets.Where(p => !draft.Presets.Contains(p.Id)).ToList();
        using (ImRaii.Disabled(available.Count == 0))
        {
            ImGui.SetNextItemWidth(-1);
            using var combo = ImRaii.Combo("##addpreset", available.Count == 0 ? "No other presets to link" : "Link a preset...");
            if (combo)
            {
                if (ImGui.IsWindowAppearing())
                    presetFilter = string.Empty;
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##pf", "Type to search...", ref presetFilter, 64);
                foreach (var p in available)
                {
                    if (presetFilter.Length > 0 && !p.Name.Contains(presetFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (ImGui.Selectable($"{p.Name}##{p.Id}"))
                        draft.Presets.Add(p.Id);
                }
            }
        }
    }

    private void Save()
    {
        draft.Text    = draft.Text.Trim();
        draft.Updated = DateTime.Now;
        var idx = Config.Notes.FindIndex(n => n.Id == draft.Id);
        if (idx >= 0) Config.Notes[idx] = draft;
        else Config.Notes.Add(draft);
        Config.Save();
        plugin.Controller.Report(isNew ? $"Note saved: {draft.Title}" : "Note updated.");
        IsOpen = false;
    }
}
