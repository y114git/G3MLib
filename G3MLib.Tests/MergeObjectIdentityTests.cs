using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.Modding.Merging;
using G3MLib.Modding.Patching;

namespace G3MLib.Tests;

public sealed class MergeObjectIdentityTests
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

    // Patches made against the same file insert at the same positions; each merged object must keep its own name and properties.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObjectsInsertedByTwoPatchesAtTheSameIndexKeepTheirOwnProperties(bool sequential)
    {
        var directory = Directory.CreateTempSubdirectory("g3mlib-objmerge-").FullName;
        try
        {
            var original = Path.Combine(directory, "original.win");
            using (var data = GameMakerData.CreateNew2026LTS())
            {
                data.GameObjects.Add(new GameMakerGameObject { Name = data.Strings.MakeString("obj_a"), Depth = 1 });
                data.GameObjects.Add(new GameMakerGameObject { Name = data.Strings.MakeString("obj_b"), Depth = 2 });
                Write(original, data);
            }

            var patches = new List<string>();
            foreach (var (name, depth, index) in new[] { ("obj_first", 100, 0), ("obj_second", 200, 0) })
            {
                var modified = Path.Combine(directory, name + ".win");
                using (var data = Read(original))
                {
                    data.GameObjects.Insert(index, new GameMakerGameObject { Name = data.Strings.MakeString(name), Depth = depth });
                    Write(modified, data);
                }
                var patch = Path.Combine(directory, name + ".g3mpatch");
                Assert.True((await PatchService.CreatePatchAsync(original, modified, patch, cancellationToken: TestContext.Current.CancellationToken)).Success);
                patches.Add(patch);
            }

            var output = Path.Combine(directory, "merged.win");
            Assert.True((await MergeService.MergePatchesAsync(original, patches, new MergeOptions { ApplyPath = output, UseSequentialMerge = sequential }, TestContext.Current.CancellationToken)).Success);

            using var merged = Read(output);
            Assert.Equal(4, merged.GameObjects.Count);
            Assert.Equal(1, merged.GameObjects.ByName("obj_a").Depth);
            Assert.Equal(2, merged.GameObjects.ByName("obj_b").Depth);
            Assert.Equal(100, merged.GameObjects.ByName("obj_first").Depth);
            Assert.Equal(200, merged.GameObjects.ByName("obj_second").Depth);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // One patch deletes an object while another edits its neighbour: the edit must land on the right object after the indexes shift.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ADeletedObjectDoesNotMoveAnotherPatchsEditToItsNeighbour(bool sequential)
    {
        var directory = Directory.CreateTempSubdirectory("g3mlib-objdel-").FullName;
        try
        {
            var original = Path.Combine(directory, "original.win");
            using (var data = GameMakerData.CreateNew2026LTS())
            {
                foreach (var (name, depth) in new[] { ("obj_a", 1), ("obj_b", 2), ("obj_c", 3) })
                    data.GameObjects.Add(new GameMakerGameObject { Name = data.Strings.MakeString(name), Depth = depth });
                Write(original, data);
            }

            var deleting = Path.Combine(directory, "deleting.win");
            using (var data = Read(original))
            {
                data.GameObjects.RemoveAt(0);
                Write(deleting, data);
            }
            var editing = Path.Combine(directory, "editing.win");
            using (var data = Read(original))
            {
                data.GameObjects.ByName("obj_c").Depth = 33;
                Write(editing, data);
            }

            var patches = new List<string>();
            foreach (var modified in new[] { deleting, editing })
            {
                var patch = Path.ChangeExtension(modified, ".g3mpatch");
                Assert.True((await PatchService.CreatePatchAsync(original, modified, patch, cancellationToken: TestContext.Current.CancellationToken)).Success);
                patches.Add(patch);
            }

            var output = Path.Combine(directory, "merged.win");
            Assert.True((await MergeService.MergePatchesAsync(original, patches, new MergeOptions { ApplyPath = output, UseSequentialMerge = sequential }, TestContext.Current.CancellationToken)).Success);

            using var merged = Read(output);
            Assert.Null(merged.GameObjects.ByName("obj_a"));
            Assert.Equal(2, merged.GameObjects.ByName("obj_b").Depth);
            Assert.Equal(33, merged.GameObjects.ByName("obj_c").Depth);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
