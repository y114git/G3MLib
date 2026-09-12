using System;
using System.IO;
using System.Threading.Tasks;
using G3MLib.DataFile;
using G3MLib.Modding.Patching;
using Xunit;

namespace G3MLib.Tests;

public sealed class PatchInputServiceTests
{
    [Fact]
    public void CopyAudioGroupsPreservesMaterializedChanges()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        string source = Path.Combine(directory, "source");
        string destination = Path.Combine(directory, "destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        try
        {
            string dataPath = Path.Combine(destination, "data.win");
            using (GameMakerData data = GameMakerData.CreateNew2026LTS())
            using (FileStream stream = File.Create(dataPath))
            {
                data.AudioGroups.Clear();
                data.AudioGroups.Add(new G3MLib.DataFile.Models.GameMakerAudioGroup { Name = data.Strings.MakeString("default") });
                data.AudioGroups.Add(new G3MLib.DataFile.Models.GameMakerAudioGroup { Name = data.Strings.MakeString("external") });
                GameMakerIO.Write(stream, data);
            }
            string sourceGroup = Path.Combine(source, "audiogroup1.dat");
            string destinationGroup = Path.Combine(destination, "audiogroup1.dat");
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
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string sourcePath = Path.Combine(directory, "source.win");
            string corruptPath = Path.Combine(directory, "corrupt.win");
            using (GameMakerData data = GameMakerData.CreateNew2026LTS())
            using (FileStream stream = File.Create(sourcePath))
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
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string sourcePath = Path.Combine(directory, "source.win");
            string unsupportedPath = Path.Combine(directory, "input.txt");
            using (GameMakerData data = GameMakerData.CreateNew2026LTS())
            using (FileStream stream = File.Create(sourcePath))
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
