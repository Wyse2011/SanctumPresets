using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SanctumPresets.Audio;

/// <summary> An audio file the user added. The file is copied into the plugin's folder. </summary>
[Serializable]
public class AudioClip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;   // inside the library folder
    public double Seconds { get; set; }
    public int Channels { get; set; }
}

/// <summary> Reads game files (templates). In the game this is Dalamud's IDataManager. </summary>
public interface IGameFiles
{
    byte[]? Get(string path);
}

/// <summary> Builds the files a slot needs to play a sound: a game sound file and a sound-only carrier effect. </summary>
public static class SoundBuilder
{
    // All three come from the player's own game install at runtime (nothing from the game is shipped with the plugin).
    public const string CarrierPath      = "vfx/monster/c0901/eff/c901sp_43se0h.avfx";            // sound-only effect: 0 particles
    public const string OnceTemplatePath = "sound/vfx/monster6/se_vfx_monster_gosetsu_depop_c.scd"; // stereo, plays once
    public const string LoopTemplatePath = "bgcommon/sound/fst/placednpc_void_loop.scd";            // the game's looping template (mono)

    public static string Key(AudioClip clip, string libraryDir, bool loop, float volume)
    {
        var src = Path.Combine(libraryDir, clip.FileName);
        var stamp = File.Exists(src) ? File.GetLastWriteTimeUtc(src).Ticks ^ new FileInfo(src).Length : 0;
        var text = FormattableString.Invariant($"{clip.Id}|{stamp}|{loop}|{volume:0.###}|v1");
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    public static string GamePathFor(string key) => $"sound/vfx/sanctum/u_{key}.scd";

    /// <summary> Build both files. Returns (scd bytes, carrier avfx bytes). </summary>
    public static (byte[] Scd, byte[] Carrier) Build(IGameFiles game, string sourceFile, bool loop, float volume, string key)
    {
        var carrierSrc = game.Get(CarrierPath) ?? throw new FileNotFoundException("Couldn't read the sound carrier effect from the game.");
        var template   = game.Get(loop ? LoopTemplatePath : OnceTemplatePath) ?? throw new FileNotFoundException("Couldn't read the sound template from the game.");
        var pcm        = AudioDecoder.Decode(sourceFile);
        var mono       = loop;                     // the game's looping template is mono
        var samples    = AudioDecoder.Prepare(pcm, volume, mono);
        var scd        = ScdBuilder.Build(template, samples, mono ? 1 : pcm.Channels, loop);

        var tree = AvfxTree.Read(carrierSrc);
        var emitter = tree.Child("Emit") ?? throw new InvalidDataException("Carrier effect has no emitter.");
        var name = emitter.Child("SdNm") ?? throw new InvalidDataException("Carrier effect has no sound field.");
        name.Data = System.Text.Encoding.ASCII.GetBytes(GamePathFor(key) + "\0");
        var life = emitter.Child("Life")?.Child("Val");
        if (life != null) life.Data = BitConverter.GetBytes(-1f);   // emitter lives as long as the effect
        return (scd, AvfxTree.Write(tree));
    }
}
