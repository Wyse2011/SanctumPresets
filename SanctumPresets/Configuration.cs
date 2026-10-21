using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;

namespace SanctumPresets;

[Serializable]
public class Preset
{
    /// <summary> Stable identity so notes stay linked through renames. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Preset";

    /// <summary> Option group name -> selected option name(s). </summary>
    public Dictionary<string, List<string>> Settings { get; set; } = new();

    public DateTime Saved { get; set; } = DateTime.Now;

    /// <summary> Per-slot colors saved with the preset (1.3+). Null for older presets. </summary>
    public Dictionary<string, ColorSettings>? SlotColors { get; set; }

    /// <summary> Single color from 1.2 presets (applied to every slot it wasn't excluded from). </summary>
    public ColorSettings? Color { get; set; }

    public Preset Clone()
        => new()
        {
            Id       = Id,
            Name     = Name,
            Saved    = Saved,
            Settings = Copy(Settings),
            Color      = Color?.Clone(),
            SlotColors = CopyColors(SlotColors),
        };

    public static Dictionary<string, ColorSettings>? CopyColors(Dictionary<string, ColorSettings>? src)
        => src?.ToDictionary(kv => kv.Key, kv => kv.Value.Clone());

    /// <summary> Short description of the colors, for tooltips and the .txt report. </summary>
    public IEnumerable<string> DescribeColors()
    {
        if (SlotColors != null)
        {
            var on = SlotColors.Where(kv => kv.Value.IsCustomized).OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToList();
            if (on.Count == 0) { yield return "original"; yield break; }
            foreach (var (slot, c) in on)
                yield return $"{slot}: {c.Describe()}";
        }
        else if (Color is { Enabled: true })
        {
            yield return "all slots: " + Color.Describe();
        }
        else
        {
            yield return "original";
        }
    }

    public static Dictionary<string, List<string>> Copy(Dictionary<string, List<string>> src)
    {
        var d = new Dictionary<string, List<string>>();
        foreach (var (k, v) in src)
            d[k] = new List<string>(v);
        return d;
    }
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 5;

    /// <summary> Penumbra directory name of the mod being controlled. Empty = auto-detect. </summary>
    public string ModDirectory { get; set; } = string.Empty;

    /// <summary> Name fragment used for auto-detection. </summary>
    public string AutoDetectName { get; set; } = "Sanctum VFX";

    public bool AutoRedraw { get; set; } = true;

    public bool ShowAllMods { get; set; } = false;

    public List<Preset> Presets { get; set; } = new();

    public List<Note> Notes { get; set; } = new();

    /// <summary> Audio files the user added (see Audio/SoundLibrary.cs). </summary>
    public List<SanctumPresets.Audio.AudioClip> AudioClips { get; set; } = new();

    /// <summary> Color for each slot (option group name -> settings). </summary>
    public Dictionary<string, ColorSettings> SlotColors { get; set; } = new();

    /// <summary> Single color from 1.2 (one color for every slot). Kept so old settings still work; moved into SlotColors on first edit. </summary>
    public ColorSettings Color { get; set; } = new();

    /// <summary> A slot's customization (color and/or speed), or null if it plays unchanged. </summary>
    public ColorSettings? ColorFor(string slot)
    {
        if (SlotColors.TryGetValue(slot, out var c))
            return c.IsCustomized ? c : null;
        if (Color.Enabled && !Color.ExcludedSlots.Contains(slot))
            return Color;
        return null;
    }

    public bool AnyColor
        => Color.Enabled || SlotColors.Values.Any(c => c.IsCustomized);

    /// <summary> Turn a 1.2 single color into per-slot colors (called before editing per slot). </summary>
    public void MigrateLegacyColor(IEnumerable<string> slots)
    {
        if (!Color.Enabled)
            return;
        foreach (var slot in slots)
        {
            if (SlotColors.ContainsKey(slot) || Color.ExcludedSlots.Contains(slot))
                continue;
            var c = Color.Clone();
            c.ExcludedSlots.Clear();
            SlotColors[slot] = c;
        }

        Color.Enabled = false;
    }

    /// <summary> Settings for one slot, created (turned off) if it doesn't exist yet. </summary>
    public ColorSettings SlotColor(string slot)
    {
        if (!SlotColors.TryGetValue(slot, out var c))
            SlotColors[slot] = c = new ColorSettings();
        return c;
    }

    /// <summary> Folder of the last .txt export, to start the save dialog there. </summary>
    public string LastExportFolder { get; set; } = string.Empty;

    /// <summary> Replaced in tests; saves through Dalamud in the game. </summary>
    public static Action<Configuration>? SaveOverride;

    public void Save()
    {
        if (SaveOverride != null) SaveOverride(this);
        else Plugin.PluginInterface.SavePluginConfig(this);
    }
}
