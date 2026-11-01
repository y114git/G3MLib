using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.Modding.Resources;

namespace G3MLib.Tests;

public sealed class AudioGroupFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"G3MLibAudio-{Guid.NewGuid():N}");

    public AudioGroupFileTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static byte[] Bytes(int seed, int length) => [.. Enumerable.Range(0, length).Select(i => (byte)(seed + i))];

    private string WriteGroup(params byte[][] entries)
    {
        using var data = GameMakerData.CreateNew2026LTS();
        foreach (var entry in entries)
            data.EmbeddedAudio.Add(new GameMakerEmbeddedAudio { Data = entry });
        var path = Path.Combine(_root, "audiogroup1.dat");
        using var stream = File.Create(path);
        GameMakerIO.Write(stream, data);
        return path;
    }

    private static List<byte[]> ReadEntries(string path)
    {
        using var stream = File.OpenRead(path);
        using var data = GameMakerIO.Read(stream);
        return [.. data.EmbeddedAudio.Select(a => a.Data)];
    }

    [Fact]
    public void SaveWithoutChangesKeepsEveryByte()
    {
        var path = WriteGroup(Bytes(1, 5), Bytes(2, 8), Bytes(3, 3));
        var copy = Path.Combine(_root, "copy.dat");
        using (var file = AudioGroupFile.Open(path, TestContext.Current.CancellationToken))
            file.Save(copy, backup: false, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(copy));
    }

    [Fact]
    public void AddReplaceRemoveAndCompactProduceAReadableFile()
    {
        var path = WriteGroup(Bytes(1, 5), Bytes(2, 8), Bytes(3, 3));
        IReadOnlyDictionary<int, int> map;
        using (var file = AudioGroupFile.Open(path, TestContext.Current.CancellationToken))
        {
            Assert.Equal(Bytes(2, 8), file.GetEntry(1).ToArray());
            file.Replace(0, Bytes(9, 4));
            file.Remove(1);
            var added = file.Add(Bytes(7, 6));
            Assert.Equal(3, added);
            map = file.Compact();
            file.Save(path, backup: true, cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Equal(new Dictionary<int, int> { [0] = 0, [2] = 1, [3] = 2 }, map);
        Assert.Equal([Bytes(9, 4), Bytes(3, 3), Bytes(7, 6)], ReadEntries(path));
        Assert.True(File.Exists(path + ".bak"));
    }

    [Fact]
    public void SoundIdRemapMovesOnlyTheGroupsSounds()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        GameMakerSound kept = new() { Name = data.Strings.MakeString("a"), GroupID = 1, AudioID = 2 };
        GameMakerSound dropped = new() { Name = data.Strings.MakeString("b"), GroupID = 1, AudioID = 1 };
        GameMakerSound other = new() { Name = data.Strings.MakeString("c"), GroupID = 2, AudioID = 2 };
        data.Sounds.Add(kept);
        data.Sounds.Add(dropped);
        data.Sounds.Add(other);

        SoundIdRemap.Apply(data, 1, new Dictionary<int, int> { [2] = 1 });

        Assert.Equal((1, -1, 2), (kept.AudioID, dropped.AudioID, other.AudioID));
    }

    [Fact]
    public void ReaderServesGroupEntriesAndExportSoundsWritesThem()
    {
        _ = WriteGroup(Bytes(1, 5), Bytes(2, 8));
        using var data = GameMakerData.CreateNew2026LTS();
        data.AudioGroups.Add(new GameMakerAudioGroup { Name = data.Strings.MakeString("default") });
        data.AudioGroups.Add(new GameMakerAudioGroup { Name = data.Strings.MakeString("group1") });
        data.Sounds.Add(new GameMakerSound { Name = data.Strings.MakeString("snd_a"), Type = data.Strings.MakeString(".ogg"), GroupID = 1, AudioID = 1 });
        var dataFile = Path.Combine(_root, "data.win");

        using (AudioGroupReader reader = new(data, dataFile))
        {
            var audio = reader.GetAudio(data.Sounds[0], out var source);
            Assert.Equal(AudioSource.GroupFile, source);
            Assert.Equal(Bytes(2, 8), audio.Value.ToArray());
        }

        var output = Path.Combine(_root, "out");
        var results = ResourceExportService.ExportSounds(data, output, dataFile, null, null, TestContext.Current.CancellationToken);
        Assert.Equal(Bytes(2, 8), File.ReadAllBytes(Path.Combine(output, "Sounds", "snd_a", "snd_a.ogg")));
        Assert.Equal(ResultKind.Written, Assert.Single(results).Kind);
    }
}
