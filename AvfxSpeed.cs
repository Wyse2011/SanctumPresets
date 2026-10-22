using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SanctumPresets;

/// <summary>
/// Changes how fast an .avfx effect plays by rewriting its timing in place (file size and structure never change).
/// Field meanings follow VFXEditor's definition of the format (checked against every place each tag is defined):
///  - time values are divided by the speed: keyframe times, lifetimes, loop points, start/end/spawn times, spawn intervals;
///  - rates are multiplied by it: velocities, rotation velocities, injection speed; gravity (an acceleration) by speed squared.
/// Curve values that are positions, angles, colors, sizes or texture-scroll offsets are untouched; retiming their
/// keyframes is what makes them change faster or slower.
/// </summary>
public static class AvfxSpeed
{
    public const float MinSpeed = 0.25f;
    public const float MaxSpeed = 4f;

    public sealed record Result(byte[] Data, int KeysRetimed, int TimesScaled, int RatesScaled);

    // Integer frame counts anywhere (scaled by 1/speed).
    private static readonly HashSet<string> IntTimes = new(StringComparer.Ordinal)
        { "LpSt", "LpEd", "StTm", "EdTm", "CrTm", "GenD", "OvrV", "OvrR", "MNRi" };

    // Particle "Simple" animation (Ptcl/Smpl) stores fixed numbers instead of curves.
    private static readonly HashSet<string> SimpleIntTimes = new(StringComparer.Ordinal) { "CrI", "CrIR", "UvIv", "CrIL", "CrLR" };
    private static readonly HashSet<string> SimpleRates    = new(StringComparer.Ordinal) { "RVX", "RVY", "RVZ", "VMin", "VMax" };
    private static readonly HashSet<string> SimpleAccels   = new(StringComparer.Ordinal) { "CGX", "CGY", "CGZ" };

    // Containers never to look inside (binary blobs and strings).
    private static readonly HashSet<string> Blobs = new(StringComparer.Ordinal)
        { "Keys", "VDrw", "VIdx", "VEmt", "VNum", "Tex" };

    private enum CurveKind { Plain, Interval, Velocity, Acceleration }

    public static Result Apply(byte[] source, float speed)
    {
        if (source.Length < 8 || Tag(source, 0) != "AVFX")
            throw new InvalidDataException("Not an .avfx file.");

        var d = (byte[])source.Clone();
        speed = Math.Clamp(speed, MinSpeed, MaxSpeed);
        if (Math.Abs(speed - 1f) < 1e-4f)
            return new Result(d, 0, 0, 0);

        var counts = new int[3];
        Walk(d, 8, d.Length, new List<string> { "AVFX" }, speed, counts);
        return new Result(d, counts[0], counts[1], counts[2]);
    }

    private static void Walk(byte[] d, int start, int end, List<string> path, float speed, int[] counts)
    {
        foreach (var (tag, off, size) in Chunks(d, start, end))
        {
            var parent = path[^1];

            if (tag == "Keys")
            {
                RetimeKeys(d, off, size, path, speed, counts);
                continue;
            }

            if (size == 4)
            {
                if (parent == "Smpl" && SimpleRates.Contains(tag) || parent == "Smpl" && SimpleAccels.Contains(tag))
                {
                    var v = BitConverter.ToSingle(d, off);
                    if (float.IsFinite(v) && v != 0f)
                    {
                        BitConverter.TryWriteBytes(d.AsSpan(off, 4), v * (SimpleAccels.Contains(tag) ? speed * speed : speed));
                        counts[2]++;
                    }
                }
                else if (IntTimes.Contains(tag) || parent == "Smpl" && SimpleIntTimes.Contains(tag))
                {
                    var v = BitConverter.ToInt32(d, off);
                    if (v > 0)
                    {
                        var nv = Math.Max(1, (int)Math.Round(v / speed, MidpointRounding.AwayFromZero));
                        BitConverter.TryWriteBytes(d.AsSpan(off, 4), nv);
                        counts[1]++;
                    }
                }
                else if (parent == "Life" && tag is "Val" or "ValR")
                {
                    var v = BitConverter.ToSingle(d, off);
                    if (float.IsFinite(v) && v > 0f) // -1 = lives forever
                    {
                        BitConverter.TryWriteBytes(d.AsSpan(off, 4), v / speed);
                        counts[1]++;
                    }
                }

                continue;
            }

            if (Blobs.Contains(tag) || size < 8 || !IsContainer(d, off, off + size))
                continue;

            path.Add(tag);
            Walk(d, off, off + size, path, speed, counts);
            path.RemoveAt(path.Count - 1);
        }
    }

    /// <summary> Which kind of quantity a curve holds, decided by its tag and where it sits. </summary>
    private static CurveKind KindOf(List<string> path)
    {
        // path ends at the chunk holding "Keys": either the curve itself, or an axis (X/Y/Z) of a multi-axis curve.
        var i = path.Count - 1;
        if (path[i] is "X" or "Y" or "Z" or "XR" or "YR" or "ZR" or "RX" or "RY" or "RZ") i--;
        if (i < 1) return CurveKind.Plain;
        var curve  = path[i];
        var owner  = path[i - 1];
        var owner2 = i >= 2 ? path[i - 2] : string.Empty;

        if (owner is "Emit" or "Ptcl")
        {
            return curve switch
            {
                "CrI" or "CrIR" when owner == "Emit"                  => CurveKind.Interval,
                "Gra" or "GraR"                                       => CurveKind.Acceleration, // gravity (on both)
                "VRX" or "VRY" or "VRZ" or "VRXR" or "VRYR" or "VRZR" => CurveKind.Velocity,     // velocity / rotation velocity
                _                                                     => CurveKind.Plain,
            };
        }

        if (owner == "Data" && owner2 == "Emit" && curve is "IjS" or "IjSR")
            return CurveKind.Velocity; // injection speed

        return CurveKind.Plain;
    }

    private static void RetimeKeys(byte[] d, int off, int size, List<string> path, float speed, int[] counts)
    {
        var kind = KindOf(path);
        var valueFactor = kind switch
        {
            CurveKind.Interval     => 1f / speed,
            CurveKind.Velocity     => speed,
            CurveKind.Acceleration => speed * speed,
            _                      => 1f,
        };

        var prevOld = -1;
        var prevNew = -1;
        for (var k = 0; k + 16 <= size; k += 16)
        {
            var p    = off + k;
            int time = BitConverter.ToUInt16(d, p);
            var nt   = (int)Math.Round(time / speed, MidpointRounding.AwayFromZero);
            // Keep keys in order and never merge two keys that were apart.
            if (prevOld >= 0 && time > prevOld && nt <= prevNew) nt = prevNew + 1;
            if (prevOld >= 0 && time == prevOld) nt = prevNew;
            nt = Math.Clamp(nt, 0, ushort.MaxValue);
            BitConverter.TryWriteBytes(d.AsSpan(p, 2), (ushort)nt);
            prevOld = time;
            prevNew = nt;
            counts[0]++;

            if (valueFactor != 1f)
            {
                var zOff = p + 12; // key: time u16, type u16, x f32, y f32, z f32 (the value)
                var z    = BitConverter.ToSingle(d, zOff);
                if (float.IsFinite(z))
                    BitConverter.TryWriteBytes(d.AsSpan(zOff, 4), z * valueFactor);
                counts[2]++;
            }
        }
    }

    // ---------- chunk helpers ----------

    /// <summary> True if the bytes are exactly a sequence of well-formed chunks (so it's safe to look inside). </summary>
    private static bool IsContainer(byte[] d, int start, int end)
    {
        var off = start;
        var any = false;
        while (off + 8 <= end)
        {
            for (var i = 0; i < 4; i++)
            {
                var c = d[off + i];
                if (c != 0 && !(c is >= (byte)'0' and <= (byte)'9' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z'))
                    return false;
            }

            if (Tag(d, off).Length == 0) return false;
            var size = BitConverter.ToInt32(d, off + 4);
            if (size < 0 || off + 8 + size > end) return false;
            var next = off + 8 + size;
            any = true;
            if (next == end) return true;            // last child, no padding counted
            off = next + ((4 - size % 4) % 4);
        }

        return any && off == end;
    }

    private static IEnumerable<(string Tag, int Offset, int Size)> Chunks(byte[] d, int off, int end)
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
        return Encoding.Latin1.GetString(b).Trim('\0');
    }
}
