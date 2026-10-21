using System;
using System.IO;

namespace SanctumPresets.Audio;

/// <summary>
/// Microsoft ADPCM encoder producing the game's layout: 128 samples per block (70 bytes per channel per block),
/// the same as every game sound file checked. Port of the encoder used for the in-game-tested chime (output is identical).
/// </summary>
public static class MsAdpcm
{
    public const int SamplesPerBlock = 128;
    private static readonly int[] C1 = { 256, 512, 0, 192, 240, 460, 392 };
    private static readonly int[] C2 = { 0, -256, 0, 64, 0, -208, -232 };
    private static readonly int[] Adapt = { 230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230 };

    /// <summary> Encode interleaved 16-bit PCM. Returns the 50-byte WAVE format block and the ADPCM data. </summary>
    public static (byte[] Format, byte[] Data, int Samples) Encode(short[] pcm, int channels, int sampleRate = 44100)
    {
        if (channels is < 1 or > 2) throw new ArgumentException("1 or 2 channels");
        var frames  = pcm.Length / channels;
        var blocks  = (frames + SamplesPerBlock - 1) / SamplesPerBlock;
        var align   = 7 * channels + (SamplesPerBlock - 2) * channels / 2;
        var data    = new byte[blocks * align];
        var chan    = new short[SamplesPerBlock];
        var nibbles = new int[channels][];
        var head0   = new short[channels];   // first sample of the block, per channel
        var head1   = new short[channels];   // second sample of the block, per channel
        var pos     = 0;
        for (var b = 0; b < blocks; b++)
        {
            var preds = new int[channels]; var deltas = new int[channels];
            for (var c = 0; c < channels; c++)
            {
                for (var i = 0; i < SamplesPerBlock; i++)
                {
                    var f = b * SamplesPerBlock + i;
                    chan[i] = f < frames ? pcm[f * channels + c] : (short)0;
                }

                (preds[c], deltas[c], nibbles[c]) = Best(chan);
                head0[c] = chan[0];
                head1[c] = chan[1];
            }

            for (var c = 0; c < channels; c++) data[pos++] = (byte)preds[c];
            for (var c = 0; c < channels; c++) { BitConverter.TryWriteBytes(data.AsSpan(pos, 2), (short)deltas[c]); pos += 2; }
            for (var c = 0; c < channels; c++) { BitConverter.TryWriteBytes(data.AsSpan(pos, 2), head1[c]); pos += 2; }
            for (var c = 0; c < channels; c++) { BitConverter.TryWriteBytes(data.AsSpan(pos, 2), head0[c]); pos += 2; }
            var count = (SamplesPerBlock - 2) * channels;
            for (var i = 0; i < count; i += 2)
            {
                var hi = nibbles[i % channels][i / channels];
                var j  = i + 1;
                var lo = nibbles[j % channels][j / channels];
                data[pos++] = (byte)((hi << 4) | lo);
            }
        }

        var fmt = new byte[50];
        using (var w = new BinaryWriter(new MemoryStream(fmt)))
        {
            w.Write((ushort)2); w.Write((ushort)channels); w.Write(sampleRate); w.Write(sampleRate * align / SamplesPerBlock);
            w.Write((ushort)align); w.Write((ushort)4); w.Write((ushort)32); w.Write((ushort)SamplesPerBlock); w.Write((ushort)7);
            for (var i = 0; i < 7; i++) { w.Write((short)C1[i]); w.Write((short)C2[i]); }
        }

        return (fmt, data, frames);
    }

    private static (int Pred, int Delta, int[] Nibbles) Best(short[] s)
    {
        long bestErr = long.MaxValue; var best = (0, 0, Array.Empty<int>());
        for (var pred = 0; pred < 7; pred++)
        {
            var p0 = (s[1] * C1[pred] + s[0] * C2[pred]) >> 8;
            var d0 = Math.Max(16, Math.Abs(s[2] - p0) / 2);
            var (nibs, err) = EncodeBlock(s, pred, d0);
            if (err < bestErr) { bestErr = err; best = (pred, d0, nibs); }
        }

        return best;
    }

    private static (int[] Nibbles, long Err) EncodeBlock(short[] s, int pred, int delta)
    {
        int s1 = s[1], s2 = s[0], c1 = C1[pred], c2 = C2[pred];
        var nibs = new int[SamplesPerBlock - 2]; long err = 0;
        for (var i = 2; i < SamplesPerBlock; i++)
        {
            var p = (s1 * c1 + s2 * c2) >> 8;
            var e = s[i] - p;
            var n = (int)Math.Round(e / (double)delta, MidpointRounding.ToEven);
            n = n < -8 ? -8 : n > 7 ? 7 : n;
            var r = p + n * delta;
            r = r < -32768 ? -32768 : r > 32767 ? 32767 : r;
            err += (long)(s[i] - r) * (s[i] - r);
            delta = Math.Max(16, (Adapt[n & 15] * delta) >> 8);
            s2 = s1; s1 = r;
            nibs[i - 2] = n & 15;
        }

        return (nibs, err);
    }
}
