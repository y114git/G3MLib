using G3MLib.DataFile;
using G3MLib.Modding.Patching;

namespace G3MLib.Tests;

public class BufferedWriteTests
{
    [Fact]
    public async Task FileOutputIsIdenticalToMemoryOutput()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        using var memory = new MemoryStream();
        GameMakerIO.Write(memory, data);

        var path = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}.win");
        try
        {
            await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
                await GameMakerIO.WriteAsync(file, data, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(memory.ToArray(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));

            await using (var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite))
                GameMakerIO.Write(file, data);
            Assert.Equal(memory.ToArray(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PatchOutputKeepsItsExternalAudioGroupsWithoutAnotherParse()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var original = Path.Combine(directory, "data.win");
            var modified = Path.Combine(directory, "mod.win");
            var patch = Path.Combine(directory, "p.g3mpatch");
            var output = Path.Combine(directory, "out", "data.win");
            using (var data = GameMakerData.CreateNew2026LTS())
                PatchInputService.WriteDataFile(data, original);
            using (var data = Read(original))
            {
                data.GeneralInfo.DefaultWindowWidth += 1;
                PatchInputService.WriteDataFile(data, modified, original);
            }
            Assert.True((await PatchService.CreatePatchAsync(original, modified, patch, cancellationToken: TestContext.Current.CancellationToken)).Success);
            Assert.True((await PatchService.ApplyPatchAsync(original, patch, output, cancellationToken: TestContext.Current.CancellationToken)).Success);
            using var applied = Read(output);
            using var source = Read(original);
            Assert.Equal(source.GeneralInfo.DefaultWindowWidth + 1, applied.GeneralInfo.DefaultWindowWidth);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static GameMakerData Read(string path)
    {
        using var stream = File.OpenRead(path);
        return GameMakerIO.Read(stream);
    }
}
