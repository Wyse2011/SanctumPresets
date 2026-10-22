using System;

namespace SanctumPresets;

/// <summary>
/// "Chroma": bakes a looping full-spectrum color cycle into every particle color curve of an effect.
/// The game animates it on its own (curve post-behavior = Repeat, as in the base game's Aether Field effect),
/// so it costs nothing at runtime and syncs through Lightless as an ordinary file.
/// Key times are in frames; FFXIV effect/animation timing is 30 frames per second.
/// </summary>
public static class AvfxChroma
{
    public const int FramesPerSecond = 30;
    public const int Steps = 12;                 // 30 degrees of hue per step, then back to the start
    public const float MinSeconds = 0.5f;
    public static readonly float MaxSeconds = ushort.MaxValue / (float)FramesPerSecond;   // key times are 16-bit frames

    private const ushort Linear = 1;             // key type: 0 Spline, 1 Linear, 2 Step
    private const int Repeat = 1;                // curve behavior: 0 Const, 1 Repeat, 2 Add

    public sealed record Result(byte[] Data, int CurvesChanged, int CycleFrames);

    public static int CycleFrames(float seconds)
        => (int)Math.Clamp(Math.Round(seconds * FramesPerSecond), Steps, ushort.MaxValue);

    public static Result Apply(byte[] source, float seconds, float strength)
    {
        strength = Math.Clamp(strength, 0f, 1f);
        var frames = CycleFrames(seconds);
        var root = AvfxTree.Read(source);
        var changed = 0;
        foreach (var ptcl in root.All("Ptcl"))
        {
            var rgb  = ptcl.Child("Col")?.Child("RGB");
            var keys = rgb?.Child("Keys");
            if (rgb == null || keys?.Data == null || keys.Data.Length < 16)
                continue;

            // The layer's own color (first key) sets the brightness and is what Strength blends from.
            var br = BitConverter.ToSingle(keys.Data, 4);
            var bg = BitConverter.ToSingle(keys.Data, 8);
            var bb = BitConverter.ToSingle(keys.Data, 12);
            if (!float.IsFinite(br) || !float.IsFinite(bg) || !float.IsFinite(bb)) continue;
            var level = Math.Max(br, Math.Max(bg, bb));
            if (level <= 1e-4f) level = 1f;

            var data = new byte[(Steps + 1) * 16];
            for (var i = 0; i <= Steps; i++)
            {
                var hue = (i % Steps) / (float)Steps;
                AvfxColor.HsvToRgb(hue, 1f, 1f, out var r, out var g, out var b);
                var t = (int)Math.Round(i * frames / (double)Steps);
                var o = i * 16;
                BitConverter.TryWriteBytes(data.AsSpan(o, 2), (ushort)t);
                BitConverter.TryWriteBytes(data.AsSpan(o + 2, 2), Linear);
                BitConverter.TryWriteBytes(data.AsSpan(o + 4, 4), br + (r * level - br) * strength);
                BitConverter.TryWriteBytes(data.AsSpan(o + 8, 4), bg + (g * level - bg) * strength);
                BitConverter.TryWriteBytes(data.AsSpan(o + 12, 4), bb + (b * level - bb) * strength);
            }

            keys.Data = data;
            rgb.Child("KeyC")?.SetInt(Steps + 1);
            var post = rgb.Child("BvPo");
            if (post != null) post.SetInt(Repeat);
            else rgb.Children!.Insert(rgb.Children.IndexOf(keys), AvfxNode.IntNode("BvPo", Repeat));
            changed++;
        }

        return new Result(changed > 0 ? AvfxTree.Write(root) : (byte[])source.Clone(), changed, frames);
    }
}
