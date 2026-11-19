using System.Collections.ObjectModel;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.Modding.Resources;

namespace G3MLib.Tests;

public class ResourceImportServiceTests
{
    [Fact]
    public async Task PatchContextFlowsAcrossAwaitAndIsolatesChildOperations()
    {
        var parent = new Modding.Patching.PatchFileSystem();
        var child = new Modding.Patching.PatchFileSystem();
        ResourceImportService.SetPatchFileSystem(parent);
        try
        {
            await Task.Run(async () =>
            {
                Assert.Same(parent, ResourceImportService.GetPatchFileSystem());
                ResourceImportService.SetPatchFileSystem(child);
                await Task.Yield();
                Assert.Same(child, ResourceImportService.GetPatchFileSystem());
            }, TestContext.Current.CancellationToken);
            Assert.Same(parent, ResourceImportService.GetPatchFileSystem());
        }
        finally
        {
            ResourceImportService.SetPatchFileSystem(null);
        }
    }

    [Fact("ResourceImportServiceTests.cs", 12)]
    public void ImportsNamedTimelineAndExtensionResources()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "timeline_test"));
        Directory.CreateDirectory(Path.Combine(directory, "extension_test"));
        try
        {
            using var data = GameMakerData.CreateNew2026LTS();
            File.WriteAllText(Path.Combine(directory, "timeline_test", "timeline_test.json"), /*lang=json*/ "{\"moments\":[{\"step\":12,\"actions\":[]}]}");
            File.WriteAllText(Path.Combine(directory, "extension_test", "extension_test.json"), /*lang=json*/ "{\"folderName\":\"extensions\",\"version\":\"1.0\",\"files\":[],\"options\":[]}");

            Assert.True(ResourceImportService.Import("Timelines", data, directory, cancellationToken: TestContext.Current.CancellationToken));
            Assert.True(ResourceImportService.Import("Extensions", data, directory, cancellationToken: TestContext.Current.CancellationToken));

            GameMakerTimeline timeline = data.Timelines.Single(timeline => timeline.Name.Content == "timeline_test");
            Assert.Equal(12u, timeline.Moments.Single().Step);
            GameMakerExtension extension = data.Extensions.Single(extension => extension.Name.Content == "extension_test");
            Assert.Equal("extensions", extension.FolderName.Content);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ImportUsesTheJsonNameInsteadOfTheFolderName()
    {
        var directory = Directory.CreateTempSubdirectory("G3MLibTests-").FullName;
        try
        {
            var pathsDirectory = Path.Combine(directory, "Paths");
            var roomsDirectory = Path.Combine(directory, "Rooms");
            var pathFolder = ResourceExportService.SafeName("pth:odd");
            var roomFolder = ResourceExportService.SafeName("CON");
            Directory.CreateDirectory(Path.Combine(pathsDirectory, pathFolder));
            Directory.CreateDirectory(Path.Combine(roomsDirectory, roomFolder));
            File.WriteAllText(Path.Combine(pathsDirectory, pathFolder, pathFolder + ".json"), /*lang=json*/ "{\"name\":\"pth:odd\"}");
            File.WriteAllText(Path.Combine(roomsDirectory, roomFolder, "room.json"), /*lang=json*/ "{\"name\":\"CON\"}");

            using var data = GameMakerData.CreateNew2026LTS();
            Assert.True(ResourceImportService.Import("Paths", data, pathsDirectory, cancellationToken: TestContext.Current.CancellationToken));
            Assert.True(ResourceImportService.Import("Rooms", data, roomsDirectory, cancellationToken: TestContext.Current.CancellationToken));
            Assert.NotNull(data.Paths.ByName("pth:odd"));
            Assert.Null(data.Paths.ByName(pathFolder));
            Assert.NotNull(data.Rooms.ByName("CON"));
            Assert.Null(data.Rooms.ByName(roomFolder));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ImportReadsAnOldStyleExportFolderWithoutTheHashSuffix()
    {
        var directory = Directory.CreateTempSubdirectory("G3MLibTests-").FullName;
        try
        {
            var pathsDirectory = Path.Combine(directory, "Paths");
            var roomsDirectory = Path.Combine(directory, "Rooms");
            // Old exports named the folder pth_odd for the resource pth:odd, and wrote no name when it matched the folder
            Directory.CreateDirectory(Path.Combine(pathsDirectory, "pth_odd"));
            Directory.CreateDirectory(Path.Combine(pathsDirectory, "pth_plain"));
            Directory.CreateDirectory(Path.Combine(roomsDirectory, "room_plain"));
            File.WriteAllText(Path.Combine(pathsDirectory, "pth_odd", "pth_odd.json"), /*lang=json*/ "{\"name\":\"pth:odd\"}");
            File.WriteAllText(Path.Combine(pathsDirectory, "pth_plain", "pth_plain.json"), /*lang=json*/ "{}");
            File.WriteAllText(Path.Combine(roomsDirectory, "room_plain", "room.json"), /*lang=json*/ "{}");

            using var data = GameMakerData.CreateNew2026LTS();
            Assert.True(ResourceImportService.Import("Paths", data, pathsDirectory, cancellationToken: TestContext.Current.CancellationToken));
            Assert.True(ResourceImportService.Import("Rooms", data, roomsDirectory, cancellationToken: TestContext.Current.CancellationToken));
            Assert.NotNull(data.Paths.ByName("pth:odd"));
            Assert.Null(data.Paths.ByName("pth_odd"));
            Assert.NotNull(data.Paths.ByName("pth_plain"));
            Assert.NotNull(data.Rooms.ByName("room_plain"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact("ResourceImportServiceTests.cs", 9)]
    public void AssetOrderRewritesCollisionEventIndexes()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var data = GameMakerData.CreateNew2026LTS();
            GameMakerGameObject parent = new()
            {
                Name = data.Strings.MakeString("obj_parent")
            };
            GameMakerGameObject target = new()
            {
                Name = data.Strings.MakeString("obj_target")
            };
            data.GameObjects.Add(parent);
            data.GameObjects.Add(target);
            ((Collection<GameMakerGameObject.Event>)(object)((Collection<GameMakerPointerList<GameMakerGameObject.Event>>)(object)parent.Events)[4]).Add(new GameMakerGameObject.Event
            {
                EventSubtype = 1u
            });
            File.WriteAllText(Path.Combine(directory, "asset_order.txt"), "@@objects@@\nobj_target\nobj_parent\n");
            ResourceImportService.ImportAssetOrder(data, directory, TestContext.Current.CancellationToken);
            Assert.Same(target, data.GameObjects[0]);
            Assert.Equal(0u, ((Collection<GameMakerPointerList<GameMakerGameObject.Event>>)(object)parent.Events)[4].Single().EventSubtype);
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

public class SequenceImportNameTests
{
    [Fact]
    public void ExportedSequenceWithAReplacedCharacterImportsUnderItsOwnName()
    {
        var directory = Directory.CreateTempSubdirectory("G3MLibTests-").FullName;
        try
        {
            using var source = GameMakerData.CreateNew2026LTS();
            source.Sequences.Add(new GameMakerSequence
            {
                Name = source.Strings.MakeString("seq:odd"),
                BroadcastMessages = [],
                Moments = [],
                Tracks = [],
                FunctionIDs = []
            });
            ResourceExportService.ExportSequences(source, directory);
            using var target = GameMakerData.CreateNew2026LTS();
            var results = ResourceImportService.Import("Sequences", target, directory, null, TestContext.Current.CancellationToken);
            Assert.Equal(ResultKind.Written, Assert.Single(results).Kind);
            Assert.NotNull(target.Sequences.ByName("seq:odd"));
            Assert.Null(target.Sequences.ByName("seq_odd"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void TilesetExportImportsBackIntoAnEmptyDataFileWithItsTileData()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");
        try
        {
            using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2023.win"));
            using var source = GameMakerIO.Read(input);
            var tileset = source.Backgrounds.First(b => b != null && b.GMS2TileWidth != 0);
            ResourceExportService.ExportTypes(source, directory, "", ["Tilesets"], TestContext.Current.CancellationToken);

            using var target = GameMakerData.CreateNew2026LTS();
            ResourceImportService.Import("Tilesets", target, Path.Combine(directory, "Tilesets"), cancellationToken: TestContext.Current.CancellationToken);

            var imported = target.Backgrounds.ByName(tileset.Name.Content);
            Assert.NotNull(imported.Texture);
            Assert.Equal((tileset.GMS2TileWidth, tileset.GMS2TileHeight, tileset.GMS2TileColumns, tileset.GMS2TileCount),
                (imported.GMS2TileWidth, imported.GMS2TileHeight, imported.GMS2TileColumns, imported.GMS2TileCount));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
