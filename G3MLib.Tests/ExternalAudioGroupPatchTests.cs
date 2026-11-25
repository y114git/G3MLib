using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.Modding.Patching;

namespace G3MLib.Tests;

public sealed class ExternalAudioGroupPatchTests
{
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

    // The audio id of a sound in an external group counts inside that group, so it may equal the slot of an embedded sound.
    [Fact]
    public async Task AnExternalGroupSoundKeepsItsNewAudioIdWhenAnEmbeddedSoundHoldsTheSameNumber()
    {
        var directory = Directory.CreateTempSubdirectory("g3mlib-extaudio-").FullName;
        try
        {
            var original = Path.Combine(directory, "original.win");
            using (var data = GameMakerData.CreateNew2026LTS())
            {
                data.AudioGroups.Add(new GameMakerAudioGroup { Name = data.Strings.MakeString("default") });
                data.AudioGroups.Add(new GameMakerAudioGroup { Name = data.Strings.MakeString("music") });
                var embedded = new GameMakerEmbeddedAudio { Data = [1, 2, 3, 4] };
                data.EmbeddedAudio.Add(embedded);
                data.Sounds.Add(new GameMakerSound { Name = data.Strings.MakeString("snd_embedded"), Type = data.Strings.MakeString(".wav"), GroupID = 0, AudioID = 0, AudioFile = embedded });
                data.Sounds.Add(new GameMakerSound { Name = data.Strings.MakeString("snd_music"), Type = data.Strings.MakeString(".ogg"), GroupID = 1, AudioGroup = data.AudioGroups[1], AudioID = 3 });
                Write(original, data);
            }

            var modified = Path.Combine(directory, "modified.win");
            using (var data = Read(original))
            {
                data.Sounds.ByName("snd_music").AudioID = 0;
                Write(modified, data);
            }

            var patch = Path.Combine(directory, "audio.g3mpatch");
            Assert.True((await PatchService.CreatePatchAsync(original, modified, patch, cancellationToken: TestContext.Current.CancellationToken)).Success);
            var output = Path.Combine(directory, "applied.win");
            Assert.True((await PatchService.ApplyPatchAsync(original, patch, output, cancellationToken: TestContext.Current.CancellationToken)).Success);

            using var applied = Read(output);
            Assert.Equal(0, applied.Sounds.ByName("snd_music").AudioID);
            Assert.Equal(1, applied.Sounds.ByName("snd_music").GroupID);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
