using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SanctumPresets;

/// <summary>
/// An .avfx file as a tree of chunks, so parts can grow or shrink (for example when keyframes are added).
/// Writing back an unmodified tree reproduces the original file byte for byte (checked on every effect in the mod).
/// </summary>
public sealed class AvfxNode
{
    public string Tag = string.Empty;
    public byte[]? Data;                 // leaf payload
    public List<AvfxNode>? Children;     // container payload
    public bool LastChildUnpadded;       // container whose last child's padding is not counted in its size
    public byte[] PadBytes = Array.Empty<byte>(); // original padding after this chunk (kept for exact round trips)

    public bool IsContainer => Children != null;

    public AvfxNode? Child(string tag)
    {
        if (Children == null) return null;
        foreach (var c in Children)
            if (c.Tag == tag) return c;
        return null;
    }

    public IEnumerable<AvfxNode> All(string tag)
    {
        if (Children == null) yield break;
        foreach (var c in Children)
            if (c.Tag == tag) yield return c;
    }

    public int Int => BitConverter.ToInt32(Data!, 0);

    public void SetInt(int v)
    {
        Data = BitConverter.GetBytes(v);
    }

    public static AvfxNode IntNode(string tag, int v) => new() { Tag = tag, Data = BitConverter.GetBytes(v) };
}

public static class AvfxTree
{
    // Payloads that are data, never chunks.
    private static readonly HashSet<string> Blobs = new(StringComparer.Ordinal) { "Keys", "VDrw", "VIdx", "VEmt", "VNum", "Tex" };

    public static AvfxNode Read(byte[] d)
    {
        if (d.Length < 8 || Tag(d, 0) != "AVFX")
            throw new InvalidDataException("Not an .avfx file.");
        var size = BitConverter.ToInt32(d, 4);
        if (size < 0 || 8 + size > d.Length)
            throw new InvalidDataException("Bad .avfx size.");
        var root = new AvfxNode { Tag = "AVFX" };
        ReadChildren(d, 8, 8 + size, root);
        root.PadBytes = d.AsSpan(8 + size).ToArray();
        return root;
    }

    private static void ReadChildren(byte[] d, int start, int end, AvfxNode parent)
    {
        parent.Children = new List<AvfxNode>();
        var off = start;
        while (off + 8 <= end)
        {
            var tag  = Tag(d, off);
            var size = BitConverter.ToInt32(d, off + 4);
            var node = new AvfxNode { Tag = tag };
            var pay  = off + 8;
            if (!Blobs.Contains(tag) && size >= 8 && IsContainer(d, pay, pay + size))
                ReadChildren(d, pay, pay + size, node);
            else
                node.Data = d.AsSpan(pay, size).ToArray();
            var next = pay + size;
            var pad  = (4 - size % 4) % 4;
            if (next == end)
            {
                parent.LastChildUnpadded = pad > 0;
                parent.Children.Add(node);
                return;
            }

            node.PadBytes = d.AsSpan(next, Math.Min(pad, end - next)).ToArray();
            parent.Children.Add(node);
            off = next + pad;
        }
    }

    public static byte[] Write(AvfxNode root)
    {
        using var ms = new MemoryStream();
        var payload = WriteChildren(root);
        ms.Write(TagBytes(root.Tag));
        ms.Write(BitConverter.GetBytes(payload.Length));
        ms.Write(payload);
        ms.Write(root.PadBytes);
        return ms.ToArray();
    }

    private static byte[] WriteChildren(AvfxNode parent)
    {
        using var ms = new MemoryStream();
        for (var i = 0; i < parent.Children!.Count; i++)
        {
            var c       = parent.Children[i];
            var payload = c.IsContainer ? WriteChildren(c) : c.Data!;
            ms.Write(TagBytes(c.Tag));
            ms.Write(BitConverter.GetBytes(payload.Length));
            ms.Write(payload);
            var last = i == parent.Children.Count - 1;
            if (last && parent.LastChildUnpadded) continue;
            var pad = (4 - payload.Length % 4) % 4;
            if (c.PadBytes.Length == pad) ms.Write(c.PadBytes);
            else ms.Write(new byte[pad]);
        }

        return ms.ToArray();
    }

    /// <summary> True if the bytes are exactly a sequence of well-formed chunks. </summary>
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
            if (next == end) return true;
            off = next + (4 - size % 4) % 4;
        }

        return any && off == end;
    }

    private static string Tag(byte[] d, int off)
    {
        Span<byte> b = stackalloc byte[4];
        for (var i = 0; i < 4; i++)
            b[i] = d[off + 3 - i];
        return Encoding.Latin1.GetString(b).Trim('\0');
    }

    /// <summary> Tags are stored with their letters reversed, zero-padded at the end: "Ver" -> "reV\0", "Ptcl" -> "lctP". </summary>
    private static byte[] TagBytes(string tag)
    {
        var s = Encoding.ASCII.GetBytes(tag);
        Array.Reverse(s);
        var b = new byte[4];
        Array.Copy(s, b, Math.Min(4, s.Length));
        return b;
    }
}
