using System;
using System.IO;
using System.Text;

namespace SanctumPresets;

public enum ColorMode
{
    /// <summary> Shift every layer's color to the chosen hue, plus a light overall tint. </summary>
    Recolor = 0,

    /// <summary> Only multiply the whole effect by the chosen color. </summary>
    Tint = 1,
}

/// <summary> Settings for coloring effects. Stored in the config and in presets. </summary>
[Serializable]
public class ColorSettings
{
    public bool Enabled { get; set; }
    public float R { get; set; } = 0.25f;
    public float G { get; set; } = 0.55f;
    public float B { get; set; } = 1.0f;
    public ColorMode Mode { get; set; } = ColorMode.Recolor;

    /// <summary> 0 = original colors, 1 = fully the chosen color. </summary>
    public float Strength { get; set; } = 1.0f;

    /// <summary> Overall brightness multiplier. </summary>
    public float Brightness { get; set; } = 1.0f;

    /// <summary> Slots (option group names) that keep their original colors. </summary>
    public System.Collections.Generic.List<string> ExcludedSlots { get; set; } = new();

    public ColorSettings Clone()
        => new()
        {
            Enabled       = Enabled,
            R             = R,
            G             = G,
            B             = B,
            Mode          = Mode,
            Strength      = Strength,
            Brightness    = Brightness,
            ExcludedSlots = new(ExcludedSlots),
        };

    /// <summary> Stable text used to name generated files, so a change in settings gives a new file. </summary>
    public string Fingerprint()
        => FormattableString.Invariant($"{(int)Mode}|{R:0.###}|{G:0.###}|{B:0.###}|{Strength:0.###}|{Brightness:0.###}");

    public string Describe()
        => FormattableString.Invariant($"#{ToByte(R):X2}{ToByte(G):X2}{ToByte(B):X2}, {(Mode == ColorMode.Recolor ? "recolor" : "tint")}, strength {Strength * 100:0}%, brightness {Brightness * 100:0}%")
         + (ExcludedSlots.Count > 0 ? $", not on: {string.Join(", ", ExcludedSlots)}" : string.Empty);

    private static int ToByte(float v)
        => (int)Math.Round(Math.Clamp(v, 0f, 1f) * 255, MidpointRounding.AwayFromZero); // same as the color wheel's hex box
}

/// <summary>
/// Recolors .avfx effect files. Works on a copy of the bytes; the structure and size never change,
/// only colour values are rewritten in place.
/// </summary>
public static class AvfxColor
{
    public sealed record Result(byte[] Data, int LayerColorsChanged, bool TintApplied);

    public static Result Apply(byte[] source, ColorSettings s)
    {
        if (source.Length < 8 || Tag(source, 0) != "AVFX")
            throw new InvalidDataException("Not an .avfx file.");

        var d        = (byte[])source.Clone();
        var strength = Math.Clamp(s.Strength, 0f, 1f);
        var bright   = Math.Clamp(s.Brightness, 0f, 5f);

        // Target color, normalized so its strongest channel is 1 (brightness is handled separately).
        var (tr, tg, tb) = (Math.Clamp(s.R, 0f, 1f), Math.Clamp(s.G, 0f, 1f), Math.Clamp(s.B, 0f, 1f));
        var max = Math.Max(tr, Math.Max(tg, tb));
        var (nr, ng, nb) = max > 1e-3f ? (tr / max, tg / max, tb / max) : (0f, 0f, 0f);
        RgbToHsv(nr, ng, nb, out var targetHue, out var targetSat, out _);

        // Overall tint: full in Tint mode, a light touch in Recolor mode.
        var tintAmount = s.Mode == ColorMode.Tint ? strength : 0.35f * strength;
        var mult = new[]
        {
            Lerp(1f, nr, tintAmount) * bright,
            Lerp(1f, ng, tintAmount) * bright,
            Lerp(1f, nb, tintAmount) * bright,
        };

        var tinted  = 0;
        var changed = 0;
        foreach (var (tag, off, size) in Chunks(d, 8, d.Length))
        {
            if (size == 4 && tag is "RvR" or "RvG" or "RvB")
            {
                var i    = tag[2] switch { 'R' => 0, 'G' => 1, _ => 2 };
                var orig = BitConverter.ToSingle(d, off);
                WriteFloat(d, off, orig * mult[i]);
                tinted++;
            }
            else if (tag == "Ptcl" && s.Mode == ColorMode.Recolor && strength > 0f)
            {
                changed += RecolorParticle(d, off, size, targetHue, targetSat, strength);
            }
        }

        return new Result(d, changed, tinted == 3);
    }

    private static int RecolorParticle(byte[] d, int start, int size, float hue, float sat, float strength)
    {
        var changed = 0;
        foreach (var (tag, off, len) in Chunks(d, start, start + size))
        {
            if (tag != "Col") continue;
            foreach (var (t2, o2, l2) in Chunks(d, off, off + len))
            {
                if (t2 != "RGB") continue;
                foreach (var (t3, o3, l3) in Chunks(d, o2, o2 + l2))
                {
                    if (t3 != "Keys") continue;
                    for (var k = 0; k + 16 <= l3; k += 16)
                    {
                        var p = o3 + k + 4; // key: [time u16][type u16][x f32][y f32][z f32]
                        var r = BitConverter.ToSingle(d, p);
                        var g = BitConverter.ToSingle(d, p + 4);
                        var b = BitConverter.ToSingle(d, p + 8);
                        if (!float.IsFinite(r) || !float.IsFinite(g) || !float.IsFinite(b)) continue;

                        var m = Math.Max(r, Math.Max(g, b));
                        if (m <= 1e-6f) continue;

                        RgbToHsv(r / m, g / m, b / m, out var h, out var sv, out var v);
                        float nr, ng, nb;
                        if (sat < 0.05f)            // white/grey chosen: drain the color
                            HsvToRgb(h, 0f, v, out nr, out ng, out nb);
                        else if (sv > 0.05f)        // colored layer: take the new hue, keep its saturation
                            HsvToRgb(hue, sv, v, out nr, out ng, out nb);
                        else
                            continue;               // white-hot layers stay white

                        WriteFloat(d, p,     Lerp(r, nr * m, strength));
                        WriteFloat(d, p + 4, Lerp(g, ng * m, strength));
                        WriteFloat(d, p + 8, Lerp(b, nb * m, strength));
                        changed++;
                    }
                }
            }
        }

        return changed;
    }

    // ---------- helpers ----------

    private static System.Collections.Generic.IEnumerable<(string Tag, int Offset, int Size)> Chunks(byte[] d, int off, int end)
    {
        while (off + 8 <= end)
        {
            var tag  = Tag(d, off);
            var size = BitConverter.ToInt32(d, off + 4);
            if (size < 0 || off + 8 + size > end)
                yield break;
            yield return (tag, off + 8, size);
            off += 8 + size + ((4 - size % 4) % 4);
        }
    }

    private static string Tag(byte[] d, int off)
    {
        Span<byte> b = stackalloc byte[4];
        for (var i = 0; i < 4; i++)
            b[i] = d[off + 3 - i];
        return Encoding.Latin1.GetString(b).Trim('\0'); // short tags ("RvR", "Col", "RGB") are zero-padded
    }

    private static void WriteFloat(byte[] d, int off, float v)
        => BitConverter.TryWriteBytes(d.AsSpan(off, 4), v);

    private static float Lerp(float a, float b, float t)
        => a + (b - a) * t;

    public static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        v = max;
        var delta = max - min;
        s = max <= 0f ? 0f : delta / max;
        if (delta <= 1e-6f) { h = 0f; return; }
        if (max == r)      h = (g - b) / delta % 6f;
        else if (max == g) h = (b - r) / delta + 2f;
        else               h = (r - g) / delta + 4f;
        h /= 6f;
        if (h < 0f) h += 1f;
    }

    public static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
    {
        h = (h % 1f + 1f) % 1f * 6f;
        var i = (int)Math.Floor(h);
        var f = h - i;
        var p = v * (1f - s);
        var q = v * (1f - s * f);
        var t = v * (1f - s * (1f - f));
        (r, g, b) = (i % 6) switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
    }
}
