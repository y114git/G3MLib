using System.Text;
using System.Text.Json;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Compiler;
using G3MLib.Modding.Merging;
using G3MLib.Modding.Patching;

namespace G3MLib.Tests;

public class MergeNameListTests
{
    private static List<string>? Merge(string[] original, string[] ours, string[] theirs) => MergeService.MergeNameLists(original, ours, theirs);

    [Fact]
    public void IdenticalEditsFromBothSidesMergeWithoutConflict()
    {
        var (merged, conflicts) = MergeService.ThreeWayMerge("a\nb\nc", "a\nB\nc\nd", "x\na\nB\nc");
        Assert.False(conflicts);
        Assert.Equal("x\na\nB\nc\nd", merged);
    }

    [Fact]
    public void DifferentEditsOfTheSameLineStillConflict()
    {
        var (_, conflicts) = MergeService.ThreeWayMerge("a\nb\nc", "a\nX\nc", "a\nY\nc");
        Assert.True(conflicts);
    }

    [Fact]
    public void AdditionsFromBothSidesAreKeptWithTheExistingSideFirst()
    {
        Assert.Equal(["a", "b", "x1", "x2", "y1", "y2"], Merge(["a", "b"], ["a", "b", "x1", "x2"], ["a", "b", "y1", "y2"]));
    }

    [Fact]
    public void AdditionsFollowTheirNearestSharedName()
    {
        Assert.Equal(["a", "y", "b", "x", "c"], Merge(["a", "b", "c"], ["a", "b", "x", "c"], ["a", "y", "b", "c"]));
    }

    [Fact]
    public void AdditionsAtTheStartStayAtTheStart()
    {
        Assert.Equal(["x", "y", "a"], Merge(["a"], ["x", "a"], ["y", "a"]));
    }

    [Fact]
    public void RemovalsFromEitherSideWin()
    {
        Assert.Equal(["a", "d", "new"], Merge(["a", "b", "c", "d"], ["a", "c", "d"], ["a", "b", "d", "new"]));
    }

    [Fact]
    public void WhenOnlyOneSideChangedTheListItsOrderStands()
    {
        Assert.Equal(["c", "b", "a"], Merge(["a", "b", "c"], ["a", "b", "c"], ["c", "b", "a"]));
        Assert.Equal(["c", "b", "a"], Merge(["a", "b", "c"], ["c", "b", "a"], ["a", "b", "c"]));
    }

    [Fact]
    public void AdditionsAfterTheLastSharedNameDoNotMoveBehindEarlierOnes()
    {
        Assert.Equal(["x", "a", "b", "y"], Merge(["a", "b"], ["x", "a", "b"], ["a", "b", "y"]));
    }

    [Fact]
    public void ReplacedNameKeepsItsPlace()
    {
        Assert.Equal(["a", "b2", "c"], Merge(["a", "b", "c"], ["a", "b", "c"], ["a", "b2", "c"]));
    }

    [Fact]
    public void BothSidesAddingTheSameNameKeepsOne()
    {
        Assert.Equal(["a", "n", "m"], Merge(["a"], ["a", "n"], ["a", "n", "m"]));
    }

    [Fact]
    public void UnchangedListsComeBackUnchanged()
    {
        string[] list = ["a", "b", "c"];
        Assert.Equal(list, Merge(list, list, list));
        Assert.Empty(Merge([], [], [])!);
    }

    [Fact]
    public void RepeatedNamesAreLeftToTheCaller()
    {
        Assert.Null(Merge(["a"], ["a", "a"], ["a"]));
    }

    [Fact]
    public void GlobalScriptFileKeepsBothSidesAndTheRestComesFromTheIncomingFile()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        var merged = MergeService.TryNameListMerge("GlobalScripts/global_scripts.json", data,
            Encoding.UTF8.GetBytes("""{"globalInitScripts":["x"],"gameEndScripts":["e1"],"extra":1}"""),
            Encoding.UTF8.GetBytes("""{"globalInitScripts":["y"],"gameEndScripts":["e2"],"extra":2}"""));
        using var doc = JsonDocument.Parse(merged!);
        Assert.Equal(["x", "y"], doc.RootElement.GetProperty("globalInitScripts").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["e1", "e2"], doc.RootElement.GetProperty("gameEndScripts").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(2, doc.RootElement.GetProperty("extra").GetInt32());
    }

    [Fact]
    public void FeatureFlagsAndTagsAreMergedByName()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        var flags = MergeService.TryNameListMerge("FeatureFlags/feature_flags.json", data, """["a"]"""u8.ToArray(), """["b"]"""u8.ToArray());
        Assert.Equal(["a", "b"], JsonSerializer.Deserialize<string[]>(flags!));

        byte[] bom = [0xEF, 0xBB, 0xBF];
        var withBom = MergeService.TryNameListMerge("FeatureFlags/feature_flags.json", data, [.. bom, .. """["a"]"""u8], """["b"]"""u8.ToArray());
        Assert.Equal(["a", "b"], JsonSerializer.Deserialize<string[]>(withBom!));

        var tags = MergeService.TryNameListMerge("Tags/tags.json", data,
            """{"tags":["one"],"assetTags":[]}"""u8.ToArray(), """{"tags":["two"],"assetTags":[{"assetId":1}]}"""u8.ToArray());
        using var doc = JsonDocument.Parse(tags!);
        Assert.Equal(["one", "two"], doc.RootElement.GetProperty("tags").EnumerateArray().Select(e => e.GetString()));
        Assert.Single(doc.RootElement.GetProperty("assetTags").EnumerateArray());
    }

    [Fact]
    public void OtherFilesAndShapesAreNotTouched()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        Assert.Null(MergeService.TryNameListMerge("Options/options.json", data, "{}"u8.ToArray(), "{}"u8.ToArray()));
        Assert.Null(MergeService.TryNameListMerge("GlobalScripts/global_scripts.json", data, """{"globalInitScripts":[1]}"""u8.ToArray(), """{"globalInitScripts":[2]}"""u8.ToArray()));
        Assert.Null(MergeService.TryNameListMerge("FeatureFlags/feature_flags.json", data, "not json"u8.ToArray(), "[]"u8.ToArray()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PatchesThatEachAppendGlobalScriptsKeepAllOfThem(bool propertyMerge)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var originalPath = Path.Combine(directory, "original.win");
            using (var original = GameMakerData.CreateNew2026LTS())
            {
                Import(original, "base_a", "base_b");
                Write(originalPath, original);
            }

            // Equal-sized additions: an index-by-index merge would let the last patch overwrite the others.
            string[] names = ["one", "two", "three"];
            var patches = new List<string>();
            foreach (var name in names)
            {
                var modPath = Path.Combine(directory, name + ".win");
                var patchPath = Path.Combine(directory, name + ".g3mpatch");
                using (var mod = Read(originalPath))
                {
                    Import(mod, name + "_catalog", name + "_main");
                    Write(modPath, mod);
                }
                Assert.True((await PatchService.CreatePatchAsync(originalPath, modPath, patchPath, cancellationToken: TestContext.Current.CancellationToken)).Success);
                patches.Add(patchPath);
            }

            var outputPath = Path.Combine(directory, "merged.win");
            Assert.True((await MergeService.MergePatchesAsync(originalPath, patches, new MergeOptions { ApplyPath = outputPath, UsePropertyMerge = propertyMerge }, TestContext.Current.CancellationToken)).Success);

            using var output = Read(outputPath);
            string[] expected = propertyMerge
                ? ["gml_GlobalScript_base_a", "gml_GlobalScript_base_b", "gml_GlobalScript_one_catalog", "gml_GlobalScript_one_main",
                   "gml_GlobalScript_two_catalog", "gml_GlobalScript_two_main", "gml_GlobalScript_three_catalog", "gml_GlobalScript_three_main"]
                // Without property merge the file of the last patch wins as a whole.
                : ["gml_GlobalScript_base_a", "gml_GlobalScript_base_b", "gml_GlobalScript_three_catalog", "gml_GlobalScript_three_main"];
            Assert.Equal(expected, output.GlobalInitScripts.Select(s => s.Code.Name.Content).ToArray());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PatchesThatEachAddARoomKeepBothInTheRoomOrder()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var originalPath = Path.Combine(directory, "original.win");
            using (var original = GameMakerData.CreateNew2026LTS())
            {
                AddRoom(original, "room_base");
                Write(originalPath, original);
            }

            var patches = new List<string>();
            foreach (var name in new[] { "room_a", "room_b" })
            {
                var modPath = Path.Combine(directory, name + ".win");
                var patchPath = Path.Combine(directory, name + ".g3mpatch");
                using (var mod = Read(originalPath))
                {
                    AddRoom(mod, name);
                    Write(modPath, mod);
                }
                Assert.True((await PatchService.CreatePatchAsync(originalPath, modPath, patchPath, cancellationToken: TestContext.Current.CancellationToken)).Success);
                patches.Add(patchPath);
            }

            var outputPath = Path.Combine(directory, "merged.win");
            Assert.True((await MergeService.MergePatchesAsync(originalPath, patches, new MergeOptions { ApplyPath = outputPath, UsePropertyMerge = true }, TestContext.Current.CancellationToken)).Success);

            using var output = Read(outputPath);
            Assert.Equal(["room_base", "room_a", "room_b"], output.GeneralInfo.RoomOrder.Select(r => r.Resource.Name.Content).ToArray());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static void AddRoom(GameMakerData data, string name)
    {
        var room = new GameMakerRoom { Name = data.Strings.MakeString(name) };
        data.Rooms.Add(room);
        data.GeneralInfo.RoomOrder.Add(new GameMakerResourceById<GameMakerRoom, GameMakerChunkROOM> { Resource = room });
    }

    private static void Import(GameMakerData data, params string[] names)
    {
        var group = new CodeImportGroup(data);
        foreach (var name in names)
            group.QueueReplace($"gml_GlobalScript_{name}", "return 1;");
        group.Import();
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
}

public class OldStyleArchiveNameTests
{
    private static PatchFileSystem ZipWith(params string[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write("{}");
            }
        }
        stream.Position = 0;
        return PatchFileSystem.LoadFromZip(stream);
    }

    [Fact]
    public void LegacySafeNameDropsTheHashSuffixOfAReplacedCharacter()
    {
        Assert.Equal("spr_a_b", ResourceExportServiceNames.Legacy("spr:a:b"));
        Assert.NotEqual("spr_a_b", ResourceExportServiceNames.Current("spr:a:b"));
        Assert.Equal("plain", ResourceExportServiceNames.Legacy("plain"));
        Assert.Equal(ResourceExportServiceNames.Current("CON"), ResourceExportServiceNames.Legacy("CON"));
    }

    [Fact]
    public void MergeFindsResourcesInOldAndNewStyleArchives()
    {
        var oldStyle = ZipWith("Sprites/spr_a_b/spr_a_b.json");
        var newStyle = ZipWith($"Sprites/{ResourceExportServiceNames.Current("spr:a:b")}/spr_a_b.json");
        Assert.True(MergeService.PatchHasResourcePayload(oldStyle, "Sprites", "spr:a:b"));
        Assert.True(MergeService.PatchHasResourcePayload(newStyle, "Sprites", "spr:a:b"));
        Assert.False(MergeService.PatchHasResourcePayload(oldStyle, "Sprites", "spr:other"));
    }
}

internal static class ResourceExportServiceNames
{
    public static string Current(string name) => G3MLib.Modding.Resources.ResourceExportService.SafeName(name);
    public static string Legacy(string name) => G3MLib.Modding.Resources.ResourceExportService.LegacySafeName(name);
}
