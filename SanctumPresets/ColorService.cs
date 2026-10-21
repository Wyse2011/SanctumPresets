using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SanctumPresets;

/// <summary>
/// Applies the chosen colors to the effects currently selected in the mod.
/// Recolored copies are written to a "_SanctumColors" subfolder inside the mod's folder and handed to Penumbra
/// as a temporary mod for the player's collection. The mod's own files are never modified.
/// The copies live inside Penumbra's mod directory on purpose: sync plugins (Lightless, Mare forks) only share
/// files from there, so other players see the colors too.
/// </summary>
public sealed class ColorService : IDisposable
{
    public const string Tag = "SanctumPresets.Color";
    public const string ColorFolderName = "_SanctumColors";
    public const int Priority = 9999;

    private readonly IPenumbra penumbra;
    private readonly Controller controller;
    private readonly Configuration config;
    private readonly string legacyDir;
    private readonly SanctumPresets.Audio.IGameFiles? game;
    private readonly string libraryDir;
    private readonly object soundLock = new();
    private readonly Dictionary<string, System.Threading.Tasks.Task> building = new();
    private readonly Dictionary<string, string> buildErrors = new();
    private string? colorDir;
    private List<string> appliedFiles = new();

    private DateTime dueAt = DateTime.MaxValue;
    private DateTime nextCheck = DateTime.MinValue;
    private Guid? appliedCollection;
    private ModIndex? index;

    public int ColoredCount { get; private set; }
    public string LastResult { get; private set; } = string.Empty;
    public bool LastResultIsError { get; private set; }

    /// <summary> Clock, replaceable in tests. </summary>
    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    /// <param name="legacyDir"> Folder used by 1.2-1.5 (outside Penumbra); emptied on first use. </param>
    public ColorService(IPenumbra penumbra, Controller controller, Configuration config, string legacyDir,
        SanctumPresets.Audio.IGameFiles? game = null, string? libraryDir = null)
    {
        this.penumbra   = penumbra;
        this.controller = controller;
        this.config     = config;
        this.legacyDir  = legacyDir;
        this.game       = game;
        this.libraryDir = libraryDir ?? legacyDir;
    }

    /// <summary> True while a sound is being converted in the background. </summary>
    public bool IsBuildingSound { get { lock (soundLock) return building.Count > 0; } }

    /// <summary> Wait for background sound conversions (used by tests). </summary>
    public void WaitForSounds()
    {
        System.Threading.Tasks.Task[] t;
        lock (soundLock) t = building.Values.ToArray();
        System.Threading.Tasks.Task.WaitAll(t);
    }

    /// <summary> Where the recolored copies currently live (null until colors are applied once). </summary>
    public string? ColorDirectory => colorDir;

    public bool Active => config.AnyColor;

    /// <summary> Ask for a re-apply after a short delay (so dragging the color wheel stays smooth). </summary>
    public void MarkDirty(double delaySeconds = 0.25)
    {
        lock (soundLock)
        {
            var due = Now().AddSeconds(delaySeconds);
            if (due < dueAt)
                dueAt = due;
        }
    }

    /// <summary> Called every frame. Applies pending changes and re-applies after logins or restarts. </summary>
    public void Update()
    {
        var now = Now();
        if (now >= dueAt)
        {
            dueAt = DateTime.MaxValue;
            ApplyNow();
            return;
        }

        // A change is pending: let it apply on schedule instead of jumping ahead.
        if (dueAt != DateTime.MaxValue)
            return;

        // Every few seconds, make sure colors are still applied (temporary mods vanish when the game or Penumbra restarts,
        // and the player's collection can change).
        if (now < nextCheck)
            return;
        nextCheck = now.AddSeconds(5);
        if (!config.AnyColor)
            return;
        if (!penumbra.IsAvailable(out _))
        {
            appliedCollection = null;
            return;
        }

        var coll = penumbra.GetPlayerCollection();
        if (coll != null && coll.Value.Id != appliedCollection)
        {
            ApplyNow();
            return;
        }

        // The copies were deleted (for example the mod was reimported, which replaces its folder): rebuild them.
        if (appliedFiles.Any(f => !File.Exists(f)))
            ApplyNow();
    }

    /// <summary> Build and apply recolored effects right away. </summary>
    public void ApplyNow()
    {
        if (!penumbra.IsAvailable(out var reason)) { Fail(reason); return; }
        var coll = penumbra.GetPlayerCollection();
        if (coll == null) { Fail("Log in to a character to use colors."); return; }

        // Switched collection: clean up the old one.
        if (appliedCollection != null && appliedCollection != coll.Value.Id)
            penumbra.RemoveTempMod(Tag, appliedCollection.Value, Priority);

        if (!config.AnyColor)
        {
            var hadColor = appliedCollection != null || ColoredCount > 0;
            penumbra.RemoveTempMod(Tag, coll.Value.Id, Priority);
            appliedCollection = null;
            ColoredCount      = 0;
            if (hadColor && config.AutoRedraw) penumbra.RedrawPlayer();
            Ok("Colors off: effects use their original colors.");
            appliedFiles = new();
            if (colorDir != null) CleanDir(colorDir, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            CleanLegacy();
            return;
        }

        var mod = controller.ResolveMod();
        if (mod == null) { Fail("Couldn't find the Sanctum VFX mod in Penumbra."); return; }
        var root = penumbra.GetModsRoot();
        if (root == null) { Fail("Couldn't find Penumbra's mod folder."); return; }
        var folder = Path.Combine(root, mod.Value.Directory);
        if (!Directory.Exists(folder)) { Fail($"Mod folder not found: {folder}"); return; }

        var newDir = Path.Combine(folder, ColorFolderName);
        if (colorDir != null && !string.Equals(colorDir, newDir, StringComparison.OrdinalIgnoreCase))
            CleanDir(colorDir, new HashSet<string>(StringComparer.OrdinalIgnoreCase)); // switched to another mod
        colorDir = newDir;
        try
        {
            Directory.CreateDirectory(colorDir);
        }
        catch (Exception e)
        {
            Fail($"Couldn't create the color folder: {e.Message}");
            return;
        }

        try
        {
            if (index == null || index.Folder != folder || index.Stamp != ModIndex.StampOf(folder))
                index = ModIndex.Load(folder);
        }
        catch (Exception e)
        {
            Fail($"Couldn't read the mod's option list: {e.Message}");
            return;
        }

        var current = penumbra.GetCurrent(coll.Value.Id, mod.Value.Directory);
        if (current == null) { Fail($"Couldn't read the mod's settings: {penumbra.LastError}"); return; }

        var paths    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var keep     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        var weak     = new List<string>();
        var pending  = new List<string>();

        foreach (var (group, options) in current.Value.Settings.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var s = config.ColorFor(group);
            if (s == null)
                continue;

            if (s.SoundId.HasValue)
            {
                // A sound replaces the slot's effect: point the slot at the sound-only carrier and the private sound path.
                var clip = config.AudioClips.FirstOrDefault(c => c.Id == s.SoundId.Value);
                var slotPath = index.SlotGamePath(group);
                if (clip == null) { problems.Add($"{group}: its sound was removed from the library"); continue; }
                if (slotPath == null) { problems.Add($"{group}: couldn't find the slot's game path"); continue; }
                var key = SanctumPresets.Audio.SoundBuilder.Key(clip, libraryDir, s.SoundLoop, s.SoundVolume);
                var scdName = $"snd_{key}.scd"; var carrierName = $"snd_{key}.avfx";
                var scdFile = Path.Combine(colorDir, scdName); var carrierFile = Path.Combine(colorDir, carrierName);
                keep.Add(scdName); keep.Add(carrierName);
                string? err;
                lock (soundLock) buildErrors.TryGetValue(key, out err);
                if (err != null) { problems.Add($"{clip.Name}: {err}"); continue; }
                if (File.Exists(scdFile) && File.Exists(carrierFile))
                {
                    paths[slotPath] = carrierFile;
                    paths[SanctumPresets.Audio.SoundBuilder.GamePathFor(key)] = scdFile;
                }
                else
                {
                    StartSoundBuild(key, clip, s.SoundLoop, s.SoundVolume, scdFile, carrierFile);
                    pending.Add(clip.Name);
                }

                continue;
            }

            foreach (var option in options.Where(o => o.Trim() != "---"))
            {
                foreach (var (gamePath, source) in index.EffectFiles(group, option))
                {
                    try
                    {
                        var bytes  = File.ReadAllBytes(source);
                        var name   = FileName(group, gamePath, bytes, s);
                        var target = Path.Combine(colorDir, name);
                        if (!File.Exists(target))
                        {
                            var data = bytes;
                            if (s.Enabled && !s.Chroma)
                            {
                                var result = AvfxColor.Apply(data, s);
                                data = result.Data;
                                if (s.Mode == ColorMode.Recolor && result.LayerColorsChanged == 0)
                                    weak.Add(option);
                            }
                            else if (s.Enabled && s.Chroma && Math.Abs(s.Brightness - 1f) > 0.001f)
                            {
                                // Chroma replaces the static color; only the brightness setting still applies.
                                data = AvfxColor.Apply(data, new ColorSettings { Enabled = true, Mode = ColorMode.Tint, Strength = 0f, Brightness = s.Brightness }).Data;
                            }

                            if (s.SpeedChanged)
                                data = AvfxSpeed.Apply(data, s.Speed).Data;

                            // Last, so the typed cycle time is exact regardless of the speed setting.
                            if (s.Chroma)
                            {
                                var chroma = AvfxChroma.Apply(data, s.ChromaSeconds, s.ChromaStrength);
                                data = chroma.Data;
                                if (chroma.CurvesChanged == 0) weak.Add(option + " (no color curve for chroma)");
                            }
                            File.WriteAllBytes(target, data);
                        }

                        paths[gamePath] = target;
                        keep.Add(name);
                    }
                    catch (Exception e)
                    {
                        problems.Add($"{option}: {e.Message}");
                    }
                }
            }
        }

        bool ok;
        if (paths.Count == 0)
            ok = penumbra.RemoveTempMod(Tag, coll.Value.Id, Priority);
        else
            ok = penumbra.AddTempMod(Tag, coll.Value.Id, paths, Priority);

        if (!ok)
        {
            Fail($"Penumbra refused the colored effects: {penumbra.LastError}");
            return;
        }

        appliedCollection = paths.Count > 0 ? coll.Value.Id : null;
        ColoredCount      = paths.Count;
        appliedFiles      = paths.Values.ToList();
        CleanDir(colorDir, keep);
        CleanLegacy();
        if (config.AutoRedraw)
            penumbra.RedrawPlayer();

        if (problems.Count > 0)
            Fail($"Customized {paths.Count} effect(s); problems: {string.Join("; ", problems)}");
        else if (pending.Count > 0)
            Ok($"Preparing sound: {string.Join(", ", pending.Distinct())}... it starts playing as soon as it's converted.");
        else if (paths.Count == 0)
            Ok("No effects selected to customize.");
        else
            Ok($"Customized {paths.Count} effect(s)." + (weak.Count > 0 ? $" Limited effect on: {string.Join(", ", weak.Distinct())}" : string.Empty));
    }

    private void StartSoundBuild(string key, SanctumPresets.Audio.AudioClip clip, bool loop, float volume, string scdFile, string carrierFile)
    {
        if (game == null) { lock (soundLock) buildErrors[key] = "sounds need the game's data (not available)"; return; }
        lock (soundLock)
        {
            if (building.ContainsKey(key)) return;
            var src = Path.Combine(libraryDir, clip.FileName);
            building[key] = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var (scd, carrier) = SanctumPresets.Audio.SoundBuilder.Build(game, src, loop, volume, key);
                    Directory.CreateDirectory(Path.GetDirectoryName(scdFile)!);
                    File.WriteAllBytes(scdFile + ".tmp", scd); File.Move(scdFile + ".tmp", scdFile, true);
                    File.WriteAllBytes(carrierFile + ".tmp", carrier); File.Move(carrierFile + ".tmp", carrierFile, true);
                }
                catch (Exception e)
                {
                    lock (soundLock) buildErrors[key] = e.Message;
                }
                finally
                {
                    lock (soundLock) building.Remove(key);
                    MarkDirty(0);
                }
            });
        }
    }

    /// <summary> Remove the colored effects from Penumbra (used when the plugin unloads). </summary>
    public void Clear()
    {
        if (appliedCollection == null)
            return;
        try
        {
            penumbra.RemoveTempMod(Tag, appliedCollection.Value, Priority);
            penumbra.RedrawPlayer();
        }
        catch
        {
            // Penumbra may already be gone; temporary mods die with it.
        }

        appliedCollection = null;
        ColoredCount      = 0;
        appliedFiles      = new();
    }

    public void Dispose()
        => Clear();

    private static string FileName(string group, string gamePath, byte[] source, ColorSettings s)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(gamePath + "|" + s.Fingerprint() + "|" + Convert.ToHexString(SHA256.HashData(source))));
        var slot = new string(group.Where(char.IsLetterOrDigit).Take(24).ToArray());
        return $"{slot}_{Convert.ToHexString(hash, 0, 8).ToLowerInvariant()}.avfx";
    }

    private static void CleanDir(string dir, HashSet<string> keep)
    {
        try
        {
            if (!Directory.Exists(dir))
                return;
            foreach (var f in Directory.EnumerateFiles(dir).Where(f => f.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".scd", StringComparison.OrdinalIgnoreCase)))
                if (!keep.Contains(Path.GetFileName(f)))
                    File.Delete(f);
            if (keep.Count == 0 && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        catch
        {
            // A file still in use will be cleaned next time.
        }
    }

    /// <summary> Remove copies left by 1.2-1.5, which kept them outside Penumbra's folder. </summary>
    private void CleanLegacy()
        => CleanDir(legacyDir, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private void Ok(string msg)   { LastResult = msg; LastResultIsError = false; }
    private void Fail(string msg) { LastResult = msg; LastResultIsError = true; }
}
