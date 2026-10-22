using System;
using System.IO;
using System.Text;

namespace SanctumPresets;

/// <summary>
/// Size and position for a whole effect, using the file's own top-level "revised" values
/// (RvSx/RvSy/RvSz = overall scale, RvPx/RvPy/RvPz = overall position offset, in yalms).
/// Changes are relative to the effect's own settings: size multiplies its scale, height and
/// forward/back are added to its offsets. Only those values change; the file's size and structure never do.
/// </summary>
public static class AvfxTransform
{
    public const float MinSize = 0.1f;
    public const float MaxSize = 10f;

    public sealed record Result(byte[] Data, int ValuesChanged);

    public static Result Apply(byte[] source, float size, float height, float forward)
    {
        if (source.Length < 8 || Tag(source, 0) != "AVFX")
            throw new InvalidDataException("Not an .avfx file.");
        var d = (byte[])source.Clone();
        size = Math.Clamp(size, MinSize, MaxSize);
        var changed = 0;
        var off = 8;
        while (off + 8 <= d.Length)
        {
            var tag  = Tag(d, off);
            var len  = BitConverter.ToInt32(d, off + 4);
            if (len < 0 || off + 8 + len > d.Length) break;
            if (len == 4)
            {
                var p = off + 8;
                var v = BitConverter.ToSingle(d, p);
                float? nv = tag switch
                {
                    "RvSx" or "RvSy" or "RvSz" when size != 1f => v * size,
                    "RvPy" when height != 0f                 => v + height,
                    "RvPz" when forward != 0f                => v + forward,
                    _ => null,
                };
                if (nv is { } x && float.IsFinite(v))
                {
                    BitConverter.TryWriteBytes(d.AsSpan(p, 4), x);
                    changed++;
                }
            }

            off += 8 + len + (4 - len % 4) % 4;
        }

        return new Result(d, changed);
    }

    private static string Tag(byte[] d, int off)
    {
        Span<byte> b = stackalloc byte[4];
        for (var i = 0; i < 4; i++) b[i] = d[off + 3 - i];
        return Encoding.Latin1.GetString(b).Trim('\0');
    }
}
