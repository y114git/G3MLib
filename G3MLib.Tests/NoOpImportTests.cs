using G3MLib.DataFile;
using G3MLib.Modding.Internal;
using G3MLib.Modding.Resources;

namespace G3MLib.Tests;

public sealed class NoOpImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    public static TheoryData<string, string> FixtureTypes()
    {
        TheoryData<string, string> data = [];
        foreach (var fixture in new[] { "data-2022.win", "data-2023.win", "data-2024.win", "data-2026.win" })
            foreach (var type in ResourceTypeRegistry.AllTypes.Where(t => t is not "CodeEntries" and not "TexturePageItems"))
                data.Add(fixture, type);
        return data;
    }

    [Theory]
    [MemberData(nameof(FixtureTypes))]
    public void ImportingAnUnchangedExportKeepsTheDataFileByteIdentical(string fixture, string type)
    {
        Directory.CreateDirectory(_root);
        var baseFile = Path.Combine(_root, fixture);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), baseFile);
        var exportDir = Path.Combine(_root, "export");

        using (var input = File.OpenRead(baseFile))
        using (var exportData = GameMakerIO.Read(input))
        {
            ResourceExportService.ExportTypes(exportData, exportDir, baseFile, [type], TestContext.Current.CancellationToken);
        }
        var typeDir = Path.Combine(exportDir, type);
        if (!Directory.Exists(typeDir))
        {
            return;
        }

        var output = Path.Combine(_root, "out.win");
        using (var input = File.OpenRead(baseFile))
        using (var data = GameMakerIO.Read(input))
        {
            ResourceImportService.SetPatchFileSystem(null);
            ResourceImportService.SetDataPaths(baseFile, output);
            ResourceImportService.Import(type, data, typeDir, TestContext.Current.CancellationToken);
            using FileStream stream = new(output, FileMode.Create);
            GameMakerIO.Write(stream, data);
        }
        using (var check = File.OpenRead(output))
        {
            using var reread = GameMakerIO.Read(check);
        }
        Assert.Equal(File.ReadAllBytes(baseFile), File.ReadAllBytes(output));
    }

    [Fact]
    public void ImportingAnEditedRoomWritesAReadableFileThatKeepsLayerAssets()
    {
        Directory.CreateDirectory(_root);
        var baseFile = Path.Combine(_root, "data-2026.win");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2026.win"), baseFile);
        using (var input = File.OpenRead(baseFile))
        using (var exportData = GameMakerIO.Read(input))
        {
            ResourceExportService.ExportTypes(exportData, Path.Combine(_root, "export"), baseFile, ["Rooms"], TestContext.Current.CancellationToken);
        }
        var roomFile = Path.Combine(_root, "export", "Rooms", "rm_probe", "room.json");
        var json = File.ReadAllText(roomFile);
        Assert.Contains("\"width\": 1366,", json, StringComparison.Ordinal);
        File.WriteAllText(roomFile, json.Replace("\"width\": 1366,", "\"width\": 2000,", StringComparison.Ordinal));

        var output = Path.Combine(_root, "out.win");
        int sequenceInstances;
        using (var input = File.OpenRead(baseFile))
        using (var data = GameMakerIO.Read(input))
        {
            sequenceInstances = data.Rooms.ByName("rm_probe").Layers.Sum(l => l.AssetsData?.Sequences?.Count ?? 0);
            ResourceImportService.SetPatchFileSystem(null);
            ResourceImportService.SetDataPaths(baseFile, output);
            ResourceImportService.Import("Rooms", data, Path.Combine(_root, "export", "Rooms"), TestContext.Current.CancellationToken);
            using FileStream stream = new(output, FileMode.Create);
            GameMakerIO.Write(stream, data);
        }
        Assert.True(sequenceInstances > 0);
        using var check = File.OpenRead(output);
        using var reread = GameMakerIO.Read(check);
        var room = reread.Rooms.ByName("rm_probe");
        Assert.Equal(2000u, room.Width);
        Assert.Equal(sequenceInstances, room.Layers.Sum(l => l.AssetsData?.Sequences?.Count ?? 0));
    }
}
