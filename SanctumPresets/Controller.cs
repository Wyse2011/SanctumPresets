using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SanctumPresets;

/// <summary> Everything the window and chat commands need, independent of UI. </summary>
public sealed class Controller
{
    public const string ExportPrefix = "SANCTUM1:";

    private readonly IPenumbra penumbra;
    private readonly Configuration config;

    public string Status { get; private set; } = string.Empty;
    public bool StatusIsError { get; private set; }

    public Controller(IPenumbra penumbra, Configuration config)
    {
        this.penumbra = penumbra;
        this.config   = config;
    }

    public IPenumbra Penumbra => penumbra;

    private void Ok(string msg)  { Status = msg; StatusIsError = false; }
    private void Err(string msg) { Status = msg; StatusIsError = true; }

    /// <summary> Find the mod to control: the saved one if it still exists, otherwise the best name match. </summary>
    public (string Directory, string Name)? ResolveMod(List<(string Directory, string Name)>? mods = null)
    {
        mods ??= penumbra.GetMods();
        if (config.ModDirectory.Length > 0)
        {
            var saved = mods.FirstOrDefault(m => m.Directory == config.ModDirectory);
            if (saved.Directory != null)
                return saved;
        }

        var key = config.AutoDetectName;
        var matches = mods.Where(m => m.Name.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
            return null;

        // Prefer the 4-slot build if both versions are installed.
        var best = matches.FirstOrDefault(m => m.Name.Contains("4-Slot", StringComparison.OrdinalIgnoreCase));
        return best.Directory != null ? best : matches[0];
    }

    /// <summary> Option groups sorted by name ("0 - Preset", "1 - VFX (Ears)", ...). </summary>
    public List<(string Group, string[] Options, int Type)> GetGroups(string modDir)
    {
        var groups = penumbra.GetGroups(modDir);
        if (groups == null)
            return new();
        return groups.Select(g => (g.Key, g.Value.Options, g.Value.Type))
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool TryContext(out (string Directory, string Name) mod, out (Guid Id, string Name) collection)
    {
        mod = default; collection = default;
        if (!penumbra.IsAvailable(out var reason)) { Err(reason); return false; }

        var m = ResolveMod();
        if (m == null) { Err($"Couldn't find a mod named like \"{config.AutoDetectName}\" in Penumbra."); return false; }

        var c = penumbra.GetPlayerCollection();
        if (c == null) { Err("Couldn't find your character's collection. Are you logged in?"); return false; }

        mod = m.Value; collection = c.Value;
        return true;
    }

    /// <summary> Read the current settings so they can be saved as a preset. </summary>
    public Dictionary<string, List<string>>? Capture()
    {
        if (!TryContext(out var mod, out var coll)) return null;
        var cur = penumbra.GetCurrent(coll.Id, mod.Directory);
        if (cur == null) { Err($"Couldn't read the mod's settings: {penumbra.LastError}"); return null; }
        return Preset.Copy(cur.Value.Settings);
    }

    /// <summary> Apply a saved preset: enable the mod, set every stored group, redraw. </summary>
    public bool Apply(Preset preset)
    {
        if (!TryContext(out var mod, out var coll)) return false;

        var groups = GetGroups(mod.Directory).ToDictionary(g => g.Group, g => g, StringComparer.Ordinal);
        penumbra.SetEnabled(coll.Id, mod.Directory, true);

        var failed = new List<string>();
        var applied = 0;
        foreach (var (group, options) in preset.Settings)
        {
            if (!groups.TryGetValue(group, out var g)) { failed.Add($"{group} (slot no longer exists)"); continue; }

            var valid = options.Where(o => g.Options.Contains(o)).ToList();
            bool ok;
            if (g.Type == 0) // single choice
            {
                var pick = valid.FirstOrDefault() ?? g.Options.FirstOrDefault();
                if (valid.Count == 0 && options.Count > 0)
                    failed.Add($"{group}: \"{options[0]}\" no longer exists");
                ok = pick != null && penumbra.SetOption(coll.Id, mod.Directory, group, pick);
            }
            else
            {
                ok = penumbra.SetOptions(coll.Id, mod.Directory, group, valid);
            }

            if (ok) applied++;
            else if (!failed.Any(f => f.StartsWith(group))) failed.Add($"{group}: {penumbra.LastError}");
        }

        if (config.AutoRedraw)
            penumbra.RedrawPlayer();

        if (failed.Count == 0)
        {
            Ok($"Applied \"{preset.Name}\" to {coll.Name}.");
            return true;
        }

        Err($"Applied \"{preset.Name}\" with problems: {string.Join("; ", failed)}");
        return applied > 0;
    }

    /// <summary> Set every single-choice group to its "---" (or first) option. </summary>
    public bool ResetAll()
    {
        if (!TryContext(out var mod, out var coll)) return false;
        var failed = 0;
        foreach (var g in GetGroups(mod.Directory))
        {
            bool ok;
            if (g.Type == 0)
            {
                var off = g.Options.FirstOrDefault(o => o.Trim() == "---") ?? g.Options.FirstOrDefault();
                ok = off == null || penumbra.SetOption(coll.Id, mod.Directory, g.Group, off);
            }
            else
            {
                ok = penumbra.SetOptions(coll.Id, mod.Directory, g.Group, Array.Empty<string>());
            }

            if (!ok) failed++;
        }

        if (config.AutoRedraw)
            penumbra.RedrawPlayer();

        if (failed == 0) { Ok("All slots reset."); return true; }
        Err($"Reset finished with {failed} problem(s): {penumbra.LastError}");
        return false;
    }

    /// <summary> Change a single slot from the window. </summary>
    public void SetSlot(string group, string option)
    {
        if (!TryContext(out var mod, out var coll)) return;
        if (penumbra.SetOption(coll.Id, mod.Directory, group, option))
        {
            if (config.AutoRedraw) penumbra.RedrawPlayer();
            Ok($"{group}: {option}");
        }
        else
        {
            Err($"Couldn't set {group}: {penumbra.LastError}");
        }
    }

    /// <summary> (slot, effect) for every slot that currently has an effect chosen. </summary>
    public List<(string Slot, string Effect)> CurrentEffects()
    {
        var cur = Capture();
        if (cur == null)
            return new();
        return cur.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(kv => kv.Value.Where(o => o.Trim() != "---").Select(o => (kv.Key, o)))
            .ToList();
    }

    /// <summary> Delete a preset and remove note links to it. </summary>
    public void DeletePreset(Preset p)
    {
        config.Presets.Remove(p);
        NoteTools.Unlink(config.Notes, p.Id);
        config.Save();
        Ok($"Deleted \"{p.Name}\".");
    }

    /// <summary> Write the notes report to a .txt file. Returns the error message, or null on success. </summary>
    public string? ExportNotes(string path, string modName)
    {
        try
        {
            if (!path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                path += ".txt";
            System.IO.File.WriteAllText(path, NoteTools.BuildReport(config, modName, DateTime.Now), new UTF8Encoding(true));
            config.LastExportFolder = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
            config.Save();
            Ok($"Notes exported to {path}");
            return null;
        }
        catch (Exception e)
        {
            Err($"Couldn't write the file: {e.Message}");
            return e.Message;
        }
    }

    public Preset? FindPreset(string name)
        => config.Presets.FirstOrDefault(p => p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    // ---------- sharing ----------

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static string Export(IEnumerable<Preset> presets)
    {
        var json = JsonSerializer.Serialize(presets.Select(p => new ExportedPreset(p.Name, p.Settings)).ToList(), Json);
        return ExportPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary> Parse presets from clipboard text. Returns null if the text isn't a Sanctum export. </summary>
    public static List<Preset>? Import(string text)
    {
        try
        {
            text = text.Trim();
            if (!text.StartsWith(ExportPrefix, StringComparison.Ordinal))
                return null;

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(text[ExportPrefix.Length..]));
            var list = JsonSerializer.Deserialize<List<ExportedPreset>>(json, Json);
            return list?.Where(e => e.Name.Length > 0 && e.Settings != null)
                .Select(e => new Preset { Name = e.Name, Settings = Preset.Copy(e.Settings), Saved = DateTime.Now })
                .ToList();
        }
        catch
        {
            return null;
        }
    }

    public void ReportImport(int added, int replaced)
        => Ok($"Imported {added} new preset(s), replaced {replaced}.");

    public void Report(string msg, bool error = false)
    {
        if (error) Err(msg); else Ok(msg);
    }

    private sealed record ExportedPreset(string Name, Dictionary<string, List<string>> Settings);
}
