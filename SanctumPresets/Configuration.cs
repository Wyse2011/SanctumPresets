using System;
using System.Collections.Generic;
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

    /// <summary> Color settings saved with the preset (null for presets made before colors existed). </summary>
    public ColorSettings? Color { get; set; }

    public Preset Clone()
        => new()
        {
            Id       = Id,
            Name     = Name,
            Saved    = Saved,
            Settings = Copy(Settings),
            Color    = Color?.Clone(),
        };

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
    public int Version { get; set; } = 3;

    /// <summary> Penumbra directory name of the mod being controlled. Empty = auto-detect. </summary>
    public string ModDirectory { get; set; } = string.Empty;

    /// <summary> Name fragment used for auto-detection. </summary>
    public string AutoDetectName { get; set; } = "Sanctum VFX";

    public bool AutoRedraw { get; set; } = true;

    public bool ShowAllMods { get; set; } = false;

    public List<Preset> Presets { get; set; } = new();

    public List<Note> Notes { get; set; } = new();

    /// <summary> Current color settings for the effects. </summary>
    public ColorSettings Color { get; set; } = new();

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
