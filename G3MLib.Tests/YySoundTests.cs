using G3MLib.DataFile;
using G3MLib.DataFile.Interop;
using G3MLib.DataFile.Models;

namespace G3MLib.Tests;

public sealed class YySoundTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "g3m-yysound-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private static GameMakerData Load(string fixture)
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        return GameMakerIO.Read(input);
    }

    private static GameMakerData RoundTrip(GameMakerData data)
    {
        using MemoryStream stream = new();
        GameMakerIO.Write(stream, data);
        stream.Position = 0;
        return GameMakerIO.Read(stream);
    }

    private string WriteSource(int compression, string file, byte[] audio)
    {
        var folder = Path.Combine(_root, "snd_test");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, file), audio);
        File.WriteAllText(Path.Combine(folder, "snd_test.yy"), $$"""
            {"$GMSound":"v2","%Name":"snd_test","audioGroupId":{"name":"audiogroup_default","path":"audiogroups/audiogroup_default",},
             "compression":{{compression}},"duration":1.5,"name":"snd_test","preload":true,"resourceType":"GMSound","soundFile":"{{file}}","volume":0.5,}
            """);
        return Path.Combine(folder, "snd_test.yy");
    }

    [Theory]
    [InlineData("data-2022.win", 0, "snd_test.wav", GameMakerSound.AudioEntryFlags.IsEmbedded | GameMakerSound.AudioEntryFlags.Regular)]
    [InlineData("data-2026.win", 1, "snd_test.ogg", GameMakerSound.AudioEntryFlags.IsCompressed | GameMakerSound.AudioEntryFlags.Regular)]
    [InlineData("data-2026.win", 2, "snd_test.ogg", GameMakerSound.AudioEntryFlags.IsEmbedded | GameMakerSound.AudioEntryFlags.IsCompressed | GameMakerSound.AudioEntryFlags.IsDecompressedOnLoad | GameMakerSound.AudioEntryFlags.Regular)]
    public void ReadsAudioAndSurvivesWriteAndRead(string fixture, int compression, string file, GameMakerSound.AudioEntryFlags flags)
    {
        byte[] audio = [.. "RIFFdata"u8, 1, 2, 3, 4];
        using var data = Load(fixture);
        var sound = YySound.Read(WriteSource(compression, file, audio), data);

        Assert.Equal(flags, sound.Flags);
        Assert.Equal(0.5f, sound.Volume);
        Assert.True(sound.Preload);
        Assert.Equal(1.5f, sound.AudioLength);
        Assert.Equal(audio, sound.AudioFile.Data);

        using var reloaded = RoundTrip(data);
        Assert.Equal(audio, reloaded.Sounds.ByName("snd_test").AudioFile.Data);

        foreach (var format in Enum.GetValues<YyFormat>())
        {
            var folder = Path.Combine(_root, "out", format.ToString());
            YySound.Write(reloaded.Sounds.ByName("snd_test"), reloaded, folder, format);
            Assert.Equal(audio, File.ReadAllBytes(Path.Combine(folder, file)));
            using var again = Load(fixture);
            var back = YySound.Read(Path.Combine(folder, "snd_test.yy"), again);
            // Older data files do not store the length, so it is whatever the data file kept.
            Assert.Equal((flags, 0.5f, reloaded.Sounds.ByName("snd_test").AudioLength), (back.Flags, back.Volume, back.AudioLength));
        }
    }

    [Fact]
    public void WavSourceForCompressedSoundIsRejected()
    {
        using var data = Load("data-2026.win");
        Assert.Throws<NotSupportedException>(() => YySound.Read(WriteSource(1, "snd_test.wav", [1]), data));
    }
}
