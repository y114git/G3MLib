using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.Modding.Resources;

namespace G3MLib.Tests;

public sealed class SoundImportBatchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"G3MLibSoundBatch-{Guid.NewGuid():N}");

    public SoundImportBatchTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        ResourceImportService.SetDataPaths(null, null);
        Directory.Delete(_root, recursive: true);
    }

    private static byte[] Bytes(int seed, int length) => [.. Enumerable.Range(0, length).Select(i => (byte)(seed + i))];

    private static byte[] WriteBytes(params byte[][] entries)
    {
        using var data = GameMakerData.CreateNew2026LTS();
        foreach (var entry in entries)
            data.EmbeddedAudio.Add(new GameMakerEmbeddedAudio { Data = entry });
        using MemoryStream stream = new();
        GameMakerIO.Write(stream, data);
        return stream.ToArray();
    }

    private static byte[][] ReadEntries(string path)
    {
        using var stream = File.OpenRead(path);
        using var data = GameMakerIO.Read(stream);
        return [.. data.EmbeddedAudio.Select(audio => audio.Data)];
    }

    private void WriteSoundFolder(string name, byte[] audio, int audioId)
    {
        var folder = Path.Combine(_root, "Sounds", name);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name + ".ogg"), audio);
        File.WriteAllText(Path.Combine(folder, name + ".json"), $"{{\"groupID\": 1, \"audioID\": {audioId}}}");
    }

    [Fact]
    public void ImportWritesNewAndReplacedSoundsIntoTheGroupFileLikeOneEditPerSound()
    {
        var original = WriteBytes(Bytes(1, 5), Bytes(2, 8), Bytes(3, 3));
        var load = Path.Combine(_root, "load");
        var save = Path.Combine(_root, "save");
        Directory.CreateDirectory(load);
        Directory.CreateDirectory(save);
        var loadData = Path.Combine(load, "data.win");
        File.WriteAllBytes(Path.Combine(load, "audiogroup1.dat"), original);

        WriteSoundFolder("snd_replace", Bytes(10, 7), 1);
        WriteSoundFolder("snd_new_a", Bytes(20, 9), -1);
        WriteSoundFolder("snd_new_b", Bytes(30, 4), -1);

        using var data = GameMakerData.CreateNew2026LTS();
        data.AudioGroups.Add(new GameMakerAudioGroup { Name = data.Strings.MakeString("default") });
        data.AudioGroups.Add(new GameMakerAudioGroup { Name = data.Strings.MakeString("group1"), Path = data.Strings.MakeString("audiogroup1.dat") });
        data.Sounds.Add(new GameMakerSound { Name = data.Strings.MakeString("snd_replace"), File = data.Strings.MakeString("snd_replace.ogg"), Type = data.Strings.MakeString(".ogg"), GroupID = 1, AudioID = 1 });

        ResourceImportService.SetPatchFileSystem(null);
        ResourceImportService.SetDataPaths(loadData, Path.Combine(save, "data.win"));
        Assert.True(ResourceImportService.Import("Sounds", data, Path.Combine(_root, "Sounds"), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal([Bytes(1, 5), Bytes(10, 7), Bytes(3, 3), Bytes(20, 9), Bytes(30, 4)], ReadEntries(Path.Combine(save, "audiogroup1.dat")));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(load, "audiogroup1.dat")));
        Assert.Equal([3, 4], new[] { "snd_new_a", "snd_new_b" }.Select(name => data.Sounds.ByName(name).AudioID));
        Assert.All(data.Sounds, sound => Assert.Null(sound.AudioFile));
        Assert.Empty(data.EmbeddedAudio);
    }

    [Fact]
    public void SoundKeepsItsEmbeddedAudioWhenTheGroupFileDoesNotExist()
    {
        WriteSoundFolder("snd_lost", Bytes(5, 6), -1);
        using var data = GameMakerData.CreateNew2026LTS();
        data.AudioGroups.Add(new GameMakerAudioGroup { Name = data.Strings.MakeString("default") });
        data.AudioGroups.Add(new GameMakerAudioGroup { Name = data.Strings.MakeString("group1"), Path = data.Strings.MakeString("audiogroup1.dat") });

        ResourceImportService.SetPatchFileSystem(null);
        ResourceImportService.SetDataPaths(Path.Combine(_root, "data.win"), Path.Combine(_root, "data.win"));
        Assert.True(ResourceImportService.Import("Sounds", data, Path.Combine(_root, "Sounds"), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(Bytes(5, 6), data.Sounds.ByName("snd_lost").AudioFile.Data);
    }
}
