using System;
using System.Collections.Generic;
using System.IO;

namespace SanctumPresets.Audio;

/// <summary> Reads .wav / .mp3 / .ogg into 16-bit PCM at 44.1 kHz, mono or stereo. </summary>
public static class AudioDecoder
{
    public const int Rate = 44100;

    public sealed record Pcm(short[] Samples, int Channels)
    {
        public double Seconds => Samples.Length / (double)Channels / Rate;
    }

    public static Pcm Decode(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        float[] samples; int channels, rate;
        switch (ext)
        {
            case ".mp3":
            {
                using var f = new NLayer.MpegFile(path);
                channels = f.Channels; rate = f.SampleRate; samples = ReadAll((buf, n) => f.ReadSamples(buf, 0, n));
                break;
            }
            case ".ogg":
            {
                using var f = new NVorbis.VorbisReader(path);
                channels = f.Channels; rate = f.SampleRate; samples = ReadAll((buf, n) => f.ReadSamples(buf, 0, n));
                break;
            }
            case ".wav":
                (samples, channels, rate) = ReadWav(File.ReadAllBytes(path));
                break;
            default:
                throw new InvalidDataException("Use an .mp3, .ogg or .wav file.");
        }

        if (channels < 1 || rate < 1000) throw new InvalidDataException("The audio file has no usable audio.");
        // Down-mix anything beyond stereo, resample to 44.1 kHz, convert to 16-bit.
        var outCh = Math.Min(2, channels);
        var frames = samples.Length / channels;
        var mixed = new float[frames * outCh];
        for (var i = 0; i < frames; i++)
        {
            if (channels <= 2)
                for (var c = 0; c < outCh; c++) mixed[i * outCh + c] = samples[i * channels + c];
            else
            {
                float l = 0, r = 0;
                for (var c = 0; c < channels; c++) { if (c % 2 == 0) l += samples[i * channels + c]; else r += samples[i * channels + c]; }
                mixed[i * 2] = l / ((channels + 1) / 2); mixed[i * 2 + 1] = r / (channels / 2);
            }
        }

        if (rate != Rate) mixed = Resample(mixed, outCh, rate, Rate);
        var pcm = new short[mixed.Length];
        for (var i = 0; i < pcm.Length; i++)
            pcm[i] = (short)Math.Clamp(Math.Round(mixed[i] * 32768f), -32768, 32767);   // same scale as reading (x / 32768), so 16-bit WAV is exact
        return new Pcm(pcm, outCh);
    }

    /// <summary> Apply volume (1 = unchanged) and optionally mix to mono. </summary>
    public static short[] Prepare(Pcm src, float volume, bool mono)
    {
        if (mono && src.Channels == 2)
        {
            var m = new short[src.Samples.Length / 2];
            for (var i = 0; i < m.Length; i++)
                m[i] = (short)Math.Clamp(Math.Round((src.Samples[2 * i] + src.Samples[2 * i + 1]) * 0.5 * volume), -32768, 32767);
            return m;
        }

        var o = new short[src.Samples.Length];
        for (var i = 0; i < o.Length; i++) o[i] = (short)Math.Clamp(Math.Round(src.Samples[i] * (double)volume), -32768, 32767);
        return o;
    }

    private static float[] ReadAll(Func<float[], int, int> read)
    {
        var list = new List<float>(1 << 20); var buf = new float[16384]; int n;
        while ((n = read(buf, buf.Length)) > 0)
            for (var i = 0; i < n; i++) list.Add(buf[i]);
        return list.ToArray();
    }

    private static float[] Resample(float[] src, int ch, int from, int to)
    {
        var frames = src.Length / ch; var outFrames = (int)((long)frames * to / from);
        var dst = new float[outFrames * ch];
        for (var i = 0; i < outFrames; i++)
        {
            var pos = i * (double)from / to; var i0 = (int)pos; var t = (float)(pos - i0); var i1 = Math.Min(i0 + 1, frames - 1);
            for (var c = 0; c < ch; c++) dst[i * ch + c] = src[i0 * ch + c] * (1 - t) + src[i1 * ch + c] * t;
        }

        return dst;
    }

    private static (float[] Samples, int Channels, int Rate) ReadWav(byte[] d)
    {
        if (d.Length < 12 || BitConverter.ToUInt32(d, 0) != 0x46464952 || BitConverter.ToUInt32(d, 8) != 0x45564157)
            throw new InvalidDataException("Not a WAV file.");
        int off = 12, fmtTag = 0, ch = 0, rate = 0, bits = 0, dataOff = -1, dataLen = 0;
        while (off + 8 <= d.Length)
        {
            var id = BitConverter.ToUInt32(d, off); var len = BitConverter.ToInt32(d, off + 4);
            if (id == 0x20746D66) { fmtTag = BitConverter.ToUInt16(d, off + 8); ch = BitConverter.ToUInt16(d, off + 10); rate = BitConverter.ToInt32(d, off + 12); bits = BitConverter.ToUInt16(d, off + 22);
                                    if (fmtTag == 0xFFFE && len >= 40) fmtTag = BitConverter.ToUInt16(d, off + 32); }
            else if (id == 0x61746164) { dataOff = off + 8; dataLen = Math.Min(len, d.Length - dataOff); break; }
            off += 8 + len + (len & 1);
        }

        if (dataOff < 0 || ch < 1) throw new InvalidDataException("WAV file has no audio data.");
        var bps = bits / 8; var n = dataLen / bps; var s = new float[n];
        for (var i = 0; i < n; i++)
        {
            var p = dataOff + i * bps;
            s[i] = (fmtTag, bits) switch
            {
                (1, 16) => BitConverter.ToInt16(d, p) / 32768f,
                (1, 24) => ((d[p] | (d[p + 1] << 8) | ((sbyte)d[p + 2] << 16))) / 8388608f,
                (1, 32) => BitConverter.ToInt32(d, p) / 2147483648f,
                (1, 8)  => (d[p] - 128) / 128f,
                (3, 32) => BitConverter.ToSingle(d, p),
                _ => throw new InvalidDataException($"Unsupported WAV format ({fmtTag}, {bits}-bit). Use 16-bit PCM, MP3 or OGG."),
            };
        }

        return (s, ch, rate);
    }
}
