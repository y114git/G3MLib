using G3MLib.DataFile;
using G3MLib.Modding.Patching;

namespace G3MLib.Tests;

public sealed class PatchInputServiceTests
{
    [Fact]
    public void CopyAudioGroupsPreservesMaterializedChanges()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        var source = Path.Combine(directory, "source");
        var destination = Path.Combine(directory, "destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        try
        {
            var dataPath = Path.Combine(destination, "data.win");
            using (var data = GameMakerData.CreateNew2026LTS())
            using (var stream = File.Create(dataPath))
            {
                data.AudioGroups.Clear();
                data.AudioGroups.Add(new DataFile.Models.GameMakerAudioGroup { Name = data.Strings.MakeString("default") });
                data.AudioGroups.Add(new DataFile.Models.GameMakerAudioGroup { Name = data.Strings.MakeString("external") });
                GameMakerIO.Write(stream, data);
            }
            var sourceGroup = Path.Combine(source, "audiogroup1.dat");
            var destinationGroup = Path.Combine(destination, "audiogroup1.dat");
            File.WriteAllBytes(sourceGroup, [1, 2, 3]);
            PatchInputService.CopyExternalAudioGroups(Path.Combine(source, "data.win"), dataPath);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(destinationGroup));

            File.WriteAllBytes(destinationGroup, [4, 5, 6]);
            PatchInputService.CopyExternalAudioGroups(Path.Combine(source, "data.win"), dataPath);
            Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(destinationGroup));
            File.Delete(destinationGroup);
            Directory.CreateDirectory(destinationGroup);
            Assert.ThrowsAny<IOException>(() => PatchInputService.CopyExternalAudioGroups(Path.Combine(source, "data.win"), dataPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact("PatchInputServiceTests.cs", 12)]
    public async Task MaterializeDataAcceptsValidDataAndRejectsCorruptData()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Combine(directory, "source.win");
            var corruptPath = Path.Combine(directory, "corrupt.win");
            using (var data = GameMakerData.CreateNew2026LTS())
            using (var stream = File.Create(sourcePath))
            {
                GameMakerIO.Write(stream, data);
            }
            await File.WriteAllTextAsync(corruptPath, "not a GameMaker data file", TestContext.Current.CancellationToken);

            Assert.Equal(sourcePath, await PatchInputService.MaterializeDataAsync(sourcePath, sourcePath, directory));
            await Assert.ThrowsAsync<InvalidDataException>(() => PatchInputService.MaterializeDataAsync(sourcePath, corruptPath, directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact("PatchInputServiceTests.cs", 39)]
    public async Task MaterializeDataRejectsUnsupportedInputsWithoutProducingData()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Combine(directory, "source.win");
            var unsupportedPath = Path.Combine(directory, "input.txt");
            using (var data = GameMakerData.CreateNew2026LTS())
            using (var stream = File.Create(sourcePath))
            {
                GameMakerIO.Write(stream, data);
            }
            await File.WriteAllTextAsync(unsupportedPath, "input", TestContext.Current.CancellationToken);

            Assert.False(PatchInputService.IsXDelta(unsupportedPath));
            await Assert.ThrowsAsync<NotSupportedException>(() => PatchInputService.MaterializeDataAsync(sourcePath, unsupportedPath, Path.Combine(directory, "output")));
            Assert.Empty(Directory.GetFiles(Path.Combine(directory, "output")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
