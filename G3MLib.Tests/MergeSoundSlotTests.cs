using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.Modding.Merging;
using G3MLib.Modding.Patching;

namespace G3MLib.Tests;

public sealed class MergeSoundSlotTests
{
    private static byte[] Audio(byte seed) => [seed, (byte)(seed + 1), (byte)(seed + 2), (byte)(seed + 3)];

    private static void AddSound(GameMakerData data, string name, byte seed, int index = -1)
    {
        var embedded = new GameMakerEmbeddedAudio { Data = Audio(seed) };
        data.EmbeddedAudio.Add(embedded);
        var sound = new GameMakerSound
        {
            Name = data.Strings.MakeString(name),
            Type = data.Strings.MakeString(".wav"),
            GroupID = 0,
            AudioID = data.EmbeddedAudio.Count - 1,
            AudioFile = embedded,
        };
        if (index < 0) data.Sounds.Add(sound);
        else data.Sounds.Insert(index, sound);
    }

    private static GameMakerData Read(string path)
    {
        using var stream = File.OpenRead(path);
        return GameMakerIO.Read(stream);
    }

    private static void Write(string path, GameMakerData data)
    {
        using var stream = File.Create(path);
        GameMakerIO.Write(stream, data);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoPatchesThatAddASoundKeepEachSoundsOwnAudio(bool sequential)
    {
        var directory = Directory.CreateTempSubdirectory("g3mlib-sndmerge-").FullName;
        try
        {
            var original = Path.Combine(directory, "original.win");
            using (var data = GameMakerData.CreateNew2026LTS())
            {
                data.AudioGroups.Add(new GameMakerAudioGroup { Name = data.Strings.MakeString("default") });
                AddSound(data, "snd_a", 10);
                AddSound(data, "snd_b", 20);
                Write(original, data);
            }

            var patches = new List<string>();
            foreach (var (name, seed) in new[] { ("snd_first", (byte)100), ("snd_second", (byte)200) })
            {
                var modified = Path.Combine(directory, name + ".win");
                using (var data = Read(original))
                {
                    AddSound(data, name, seed, 0);
                    Write(modified, data);
                }
                var patch = Path.Combine(directory, name + ".g3mpatch");
                Assert.True((await PatchService.CreatePatchAsync(original, modified, patch, cancellationToken: TestContext.Current.CancellationToken)).Success);
                patches.Add(patch);
            }

            var output = Path.Combine(directory, "merged.win");
            Assert.True((await MergeService.MergePatchesAsync(original, patches, new MergeOptions { ApplyPath = output, UseSequentialMerge = sequential }, TestContext.Current.CancellationToken)).Success);

            using var merged = Read(output);
            Assert.Equal(Audio(10), merged.Sounds.ByName("snd_a").AudioFile!.Data);
            Assert.Equal(Audio(20), merged.Sounds.ByName("snd_b").AudioFile!.Data);
            Assert.Equal(Audio(100), merged.Sounds.ByName("snd_first").AudioFile!.Data);
            Assert.Equal(Audio(200), merged.Sounds.ByName("snd_second").AudioFile!.Data);
            Assert.Equal(4, merged.Sounds.Select(s => s.AudioID).Distinct().Count());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
