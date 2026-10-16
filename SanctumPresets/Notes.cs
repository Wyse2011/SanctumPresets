using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SanctumPresets;

public enum NoteKind
{
    Fix     = 0,
    Idea    = 1,
    General = 2,
}

/// <summary> A link from a note to an effect, optionally in one specific slot. </summary>
[Serializable]
public class EffectLink
{
    /// <summary> The effect's option name, e.g. "[Mist] Ground Mist - b0183_mlw1_o". </summary>
    public string Effect { get; set; } = string.Empty;

    /// <summary> Slot (option group) name, or empty for "any slot". </summary>
    public string Slot { get; set; } = string.Empty;

    public bool Matches(string effect, string slot)
        => Effect == effect && (Slot.Length == 0 || Slot == slot);

    public override string ToString()
        => Slot.Length == 0 ? Effect : $"{Effect}  [{Slot}]";
}

[Serializable]
public class Note
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public NoteKind Kind { get; set; } = NoteKind.Fix;
    public string Text { get; set; } = string.Empty;
    public bool Done { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Updated { get; set; } = DateTime.Now;
    public List<EffectLink> Effects { get; set; } = new();
    public List<Guid> Presets { get; set; } = new();

    public Note Clone()
        => new()
        {
            Id      = Id,
            Kind    = Kind,
            Text    = Text,
            Done    = Done,
            Created = Created,
            Updated = Updated,
            Effects = Effects.Select(e => new EffectLink { Effect = e.Effect, Slot = e.Slot }).ToList(),
            Presets = new List<Guid>(Presets),
        };

    /// <summary> First line of the text, for compact lists. </summary>
    public string Title
    {
        get
        {
            var t = Text.Trim();
            var nl = t.IndexOf('\n');
            var first = nl < 0 ? t : t[..nl].TrimEnd('\r');
            return first.Length == 0 ? "(empty note)" : first;
        }
    }

    public static string KindLabel(NoteKind k)
        => k switch
        {
            NoteKind.Fix  => "Fix needed",
            NoteKind.Idea => "Idea",
            _             => "General",
        };
}

public static class NoteTools
{
    /// <summary> Open notes linked to this effect (in this slot or "any slot"). </summary>
    public static List<Note> OpenFor(IEnumerable<Note> notes, string effect, string slot)
        => notes.Where(n => !n.Done && n.Effects.Any(l => l.Matches(effect, slot))).ToList();

    /// <summary> Notes (open and done) linked to a preset. </summary>
    public static List<Note> ForPreset(IEnumerable<Note> notes, Guid presetId)
        => notes.Where(n => n.Presets.Contains(presetId)).ToList();

    /// <summary> Remove links to a deleted preset. </summary>
    public static void Unlink(IEnumerable<Note> notes, Guid presetId)
    {
        foreach (var n in notes)
            n.Presets.Remove(presetId);
    }

    /// <summary> Build the plain-text report written to .txt. </summary>
    public static string BuildReport(Configuration cfg, string modName, DateTime now)
    {
        var sb = new StringBuilder();
        var presetNames = cfg.Presets.ToDictionary(p => p.Id, p => p.Name);

        sb.AppendLine("SANCTUM VFX - NOTES");
        sb.AppendLine($"Exported {now:yyyy-MM-dd HH:mm}");
        if (modName.Length > 0)
            sb.AppendLine($"Mod: {modName}");
        var open = cfg.Notes.Count(n => !n.Done);
        sb.AppendLine($"{open} open note(s), {cfg.Notes.Count - open} done, {cfg.Presets.Count} preset(s)");
        sb.AppendLine();

        void Section(string title, IEnumerable<Note> notes)
        {
            var list = notes.OrderBy(n => n.Created).ToList();
            sb.AppendLine($"=== {title} ({list.Count}) ===");
            if (list.Count == 0)
            {
                sb.AppendLine("(none)");
                sb.AppendLine();
                return;
            }

            var i = 1;
            foreach (var n in list)
            {
                var lines = n.Text.Replace("\r", string.Empty).Trim().Split('\n');
                sb.AppendLine($"{i++}. [{(n.Done ? "x" : " ")}] {lines[0]}");
                foreach (var extra in lines.Skip(1))
                    sb.AppendLine($"       {extra}");
                foreach (var e in n.Effects)
                    sb.AppendLine($"       Effect: {e.Effect}" + (e.Slot.Length > 0 ? $"   (slot: {e.Slot})" : "   (any slot)"));
                foreach (var p in n.Presets)
                    sb.AppendLine($"       Preset: {(presetNames.TryGetValue(p, out var name) ? name : "(deleted preset)")}");
                if (n.Done && title != "DONE")
                    sb.AppendLine("       (done)");
                sb.AppendLine($"       Added {n.Created:yyyy-MM-dd}" + (n.Updated > n.Created.AddMinutes(1) ? $", edited {n.Updated:yyyy-MM-dd}" : string.Empty));
                sb.AppendLine();
            }
        }

        Section("FIXES NEEDED", cfg.Notes.Where(n => !n.Done && n.Kind == NoteKind.Fix));
        Section("IDEAS", cfg.Notes.Where(n => !n.Done && n.Kind == NoteKind.Idea));
        Section("GENERAL NOTES", cfg.Notes.Where(n => !n.Done && n.Kind == NoteKind.General));
        Section("DONE", cfg.Notes.Where(n => n.Done));

        sb.AppendLine($"=== PRESETS ({cfg.Presets.Count}) ===");
        if (cfg.Presets.Count == 0)
            sb.AppendLine("(none)");
        foreach (var p in cfg.Presets)
        {
            sb.AppendLine($"* {p.Name}   (saved {p.Saved:yyyy-MM-dd})");
            foreach (var (slot, opts) in p.Settings.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"     {slot}: {(opts.Count == 0 ? "---" : string.Join(", ", opts))}");
            var linked = ForPreset(cfg.Notes, p.Id);
            foreach (var n in linked)
                sb.AppendLine($"     Note ({Note.KindLabel(n.Kind)}{(n.Done ? ", done" : string.Empty)}): {n.Title}");
            sb.AppendLine();
        }

        return sb.ToString().Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
    }
}
