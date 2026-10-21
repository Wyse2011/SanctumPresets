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
        return bytes;
    }
}
