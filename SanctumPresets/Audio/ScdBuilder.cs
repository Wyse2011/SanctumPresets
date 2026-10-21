using System;
using System.IO;

namespace SanctumPresets.Audio;

/// <summary>
/// Builds a game sound file (.scd) by taking a real game sound file as the template and replacing only its audio.
/// The template's header, sound, track and layout entries (volume, bus, 3D settings) are kept byte for byte.
/// Supports templates with one sound/track/audio entry whose audio is MS-ADPCM and sits last in the file.
/// </summary>
public static class ScdBuilder
{
    private const int AudioFormatMsAdpcm = 0x0C;

    public static int AudioEntryOffset(byte[] t)
    {
        if (t.Length < 0x60 || BitConverter.ToUInt32(t, 0) != 0x42444553 || BitConverter.ToUInt32(t, 4) != 0x46435353) // "SEDB" "SSCF"
            throw new InvalidDataException("Not a game sound file.");
        var counts = (BitConverter.ToInt16(t, 0x30), BitConverter.ToInt16(t, 0x32), BitConverter.ToInt16(t, 0x34));
        if (counts != (1, 1, 1)) throw new InvalidDataException("Template must have exactly one sound, track and audio entry.");
        var audioTable = BitConverter.ToInt32(t, 0x30 + 12);
        var audio      = BitConverter.ToInt32(t, audioTable);
        if (BitConverter.ToInt32(t, audio + 12) != AudioFormatMsAdpcm || BitConverter.ToInt32(t, audio + 24) != 50)
            throw new InvalidDataException("Template audio is not MS-ADPCM.");
        return audio;
    }

    /// <summary> pcm: interleaved 16-bit samples at 44.1 kHz. loop: loop the whole sound. </summary>
    public static byte[] Build(byte[] template, short[] pcm, int channels, bool loop)
    {
        var a = AudioEntryOffset(template);
        var flags = BitConverter.ToInt32(template, a + 28);
        var (fmt, data, frames) = MsAdpcm.Encode(pcm, channels);
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        w.Write(template, 0, a);
        w.Write(data.Length); w.Write(channels); w.Write(44100); w.Write(AudioFormatMsAdpcm);
        w.Write(0); w.Write(loop ? frames - 1 : 0); w.Write(50); w.Write(flags);
        w.Write(fmt); w.Write(data);
        while (ms.Length % 16 != 0) w.Write((byte)0);
        var bytes = ms.ToArray();
        BitConverter.TryWriteBytes(bytes.AsSpan(0x10, 4), bytes.Length);
        SetTrackTiming(bytes, (int)Math.Round(frames * 1000.0 / 44100), loop);
        return bytes;
    }

    /// <summary>
    /// The track is a small script whose waits are in milliseconds and must match the audio
    /// (the templates' waits match their own audio, not ours):
    ///   plays once: KeyOn(07) -> Interval(03, ms) -> KeyOff(0C)                                   : interval = audio length
    ///   loop:       KeyOn(07) -> Interval(03, ms) -> LoopStart(0D) -> Interval(03, ms) -> LoopEnd(0E) : start 1 ms, loop = audio length
    /// </summary>
    public static void SetTrackTiming(byte[] d, int lengthMs, bool loop)
    {
        var trackTable = BitConverter.ToInt32(d, 0x30 + 8);
        var t0 = BitConverter.ToInt32(d, trackTable);
        var a0 = AudioEntryOffset(d);
        var k = IndexOf(d, t0, a0, new byte[] { 0x07, 0x00, 0x03, 0x00 });
        if (k < 0) throw new InvalidDataException("Template track has no start/wait commands.");
        if (!loop)
        {
            if (d[k + 8] != 0x0C || d[k + 9] != 0x00) throw new InvalidDataException("Template track is not a play-once track.");
            BitConverter.TryWriteBytes(d.AsSpan(k + 4, 4), lengthMs);
            return;
        }

        var ls = k + 8;
        if (d[ls] != 0x0D || d[ls + 1] != 0x00 || d[ls + 10] != 0x03 || d[ls + 11] != 0x00 || d[ls + 16] != 0x0E || d[ls + 17] != 0x00)
            throw new InvalidDataException("Template track is not a looping track.");
        BitConverter.TryWriteBytes(d.AsSpan(k + 4, 4), 1);
        BitConverter.TryWriteBytes(d.AsSpan(ls + 12, 4), lengthMs);
    }

    private static int IndexOf(byte[] d, int start, int end, byte[] pattern)
    {
        for (var i = start; i + pattern.Length <= end; i++)
        {
            var ok = true;
            for (var j = 0; j < pattern.Length && ok; j++) ok = d[i + j] == pattern[j];
            if (ok) return i;
        }

        return -1;
    }
}
