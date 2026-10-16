using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SanctumPresets;

/// <summary>
/// Reads a Penumbra mod folder to learn which files each option uses.
/// Supports groups stored inline in meta.json (Penumbra 1.7+) and the older group_*.json files.
/// </summary>
public sealed class ModIndex
{
    /// <summary> group -> option -> (game path -> path relative to the mod folder). </summary>
    public Dictionary<string, Dictionary<string, Dictionary<string, string>>> Groups { get; } = new(StringComparer.Ordinal);

    public string Folder { get; private init; } = string.Empty;
    public DateTime Stamp { get; private init; }

    private static readonly JsonDocumentOptions JsonOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    public static ModIndex Load(string folder)
    {
        var index = new ModIndex { Folder = folder, Stamp = StampOf(folder) };
        var meta = Path.Combine(folder, "meta.json");
        var foundInline = false;
        if (File.Exists(meta))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(meta), JsonOptions);
            if (doc.RootElement.TryGetProperty("Groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in groups.EnumerateArray())
                    index.AddGroup(g);
                foundInline = true;
            }
        }

        if (!foundInline)
        {
            foreach (var file in Directory.EnumerateFiles(folder, "group_*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file), JsonOptions);
                index.AddGroup(doc.RootElement);
            }
        }

        return index;
    }

    /// <summary> Latest write time of the files that describe the mod, used to know when to reload. </summary>
    public static DateTime StampOf(string folder)
    {
        if (!Directory.Exists(folder))
            return DateTime.MinValue;
        var files = Directory.EnumerateFiles(folder, "*.json");
        return files.Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
    }

    private void AddGroup(JsonElement g)
    {
        if (!g.TryGetProperty("Name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            return;
        var options = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        if (g.TryGetProperty("Options", out var opts) && opts.ValueKind == JsonValueKind.Array)
        {
            foreach (var o in opts.EnumerateArray())
            {
                if (!o.TryGetProperty("Name", out var on) || on.ValueKind != JsonValueKind.String)
                    continue;
                var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (o.TryGetProperty("Files", out var f) && f.ValueKind == JsonValueKind.Object)
                    foreach (var p in f.EnumerateObject())
                        if (p.Value.ValueKind == JsonValueKind.String)
                            files[p.Name] = p.Value.GetString()!;
                options[on.GetString()!] = files;
            }
        }

        Groups[nameEl.GetString()!] = options;
    }

    /// <summary> Effect files (.avfx) an option uses: (game path, absolute file path on disk). </summary>
    public List<(string GamePath, string FullPath)> EffectFiles(string group, string option)
    {
        var list = new List<(string, string)>();
        if (!Groups.TryGetValue(group, out var options) || !options.TryGetValue(option, out var files))
            return list;

        foreach (var (game, rel) in files)
        {
            if (!game.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase))
                continue;
            var parts = rel.Split('\\', '/', StringSplitOptions.RemoveEmptyEntries);
            list.Add((game, Path.Combine(new[] { Folder }.Concat(parts).ToArray())));
        }

        return list;
    }
}
