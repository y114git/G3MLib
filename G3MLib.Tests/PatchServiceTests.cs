using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.Modding.Diffing;
using G3MLib.Modding.Merging;
using G3MLib.Modding.Patching;
using Xunit;

namespace G3MLib.Tests;

public class PatchServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PatchManifestsRecordLibraryVersionAndAcceptLegacyInputs(bool sequential)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string original = Path.Combine(directory, "original.win");
            string modified = Path.Combine(directory, "modified.win");
            string patch = Path.Combine(directory, "change.g3mpatch");
            string merged = Path.Combine(directory, "merged.g3mpatch");
            using (var data = GameMakerData.CreateNew2026LTS())
            {
                Write(original, data);
                data.Rooms[0].Width++;
                Write(modified, data);
            }
            var created = await PatchService.CreatePatchAsync(original, modified, patch);
            Assert.True(created.Success, created.Error);
            using (var archive = System.IO.Compression.ZipFile.Open(patch, System.IO.Compression.ZipArchiveMode.Update))
            {
                var entry = archive.GetEntry("g3mpatch.json")!;
                System.Text.Json.Nodes.JsonObject manifest;
                using (var stream = entry.Open())
                    manifest = System.Text.Json.Nodes.JsonNode.Parse(stream)!.AsObject();
                Assert.Equal("G3MLib", manifest["library"]!["name"]!.GetValue<string>());
                Assert.Equal(G3MLib.Modding.ModdingMetadata.LibraryVersion, manifest["library"]!["version"]!.GetValue<string>());
                manifest.Remove("library");
                Assert.Null(System.Text.Json.JsonSerializer.Deserialize<G3MPatchManifest>(manifest.ToJsonString())!.Library);
                entry.Delete();
                using var output = archive.CreateEntry("g3mpatch.json").Open();
                System.Text.Json.JsonSerializer.Serialize(output, manifest);
            }
            var applied = await PatchService.ApplyPatchAsync(original, patch, Path.Combine(directory, "applied.win"));
            Assert.True(applied.Success, applied.Error);
            var result = await MergeService.MergePatchesAsync(original, [patch, patch], new MergeOptions
            {
                OutputPath = merged,
                UseSequentialMerge = sequential
            });
            Assert.True(result.Success, result.Error);
            using var mergedArchive = System.IO.Compression.ZipFile.OpenRead(merged);
            using var manifestStream = mergedArchive.GetEntry("g3mpatch.json")!.Open();
            var mergedManifest = System.Text.Json.JsonSerializer.Deserialize<G3MPatchManifest>(manifestStream)!;
            Assert.Equal("G3MLib", mergedManifest.Library!.Name);
            Assert.Equal(G3MLib.Modding.ModdingMetadata.LibraryVersion, mergedManifest.Library.Version);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SequentialMergeRejectsPropertyMerging()
    {
        MergeResult result = await MergeService.MergePatchesAsync("unused.win", [], new MergeOptions { UseSequentialMerge = true, UsePropertyMerge = true });
        Assert.False(result.Success);
        Assert.Equal("Sequential merge does not support code or property merging.", result.Error);
    }

    [Fact]
    public async Task MergeDataInputsSupportsOverlayWithoutManifest()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string original = Path.Combine(directory, "original.win");
            string modified = Path.Combine(directory, "modified.win");
            string output = Path.Combine(directory, "merged.win");
            using (GameMakerData data = GameMakerData.CreateNew2026LTS())
            {
                Write(original, data);
                data.GeneralInfo.LastObj = 321;
                Write(modified, data);
            }

            MergeResult result = await MergeService.MergePatchesAsync(original, [modified, modified], new MergeOptions { ApplyPath = output });

            Assert.True(result.Success, result.Error);
            using GameMakerData merged = Read(output);
            Assert.Equal(321u, merged.GeneralInfo.LastObj);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidPatchPreservesExistingOutputAndOriginal()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string original = Path.Combine(directory, "original.win");
            string patch = Path.Combine(directory, "invalid.g3mpatch");
            using (GameMakerData data = GameMakerData.CreateNew2026LTS())
                Write(original, data);
            byte[] expected = await File.ReadAllBytesAsync(original, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(patch, "invalid zip", TestContext.Current.CancellationToken);

            PatchApplyResult result = await PatchService.ApplyPatchAsync(original, patch, original);

            Assert.False(result.Success);
            Assert.True(File.Exists(original));
            Assert.Equal(expected, await File.ReadAllBytesAsync(original, TestContext.Current.CancellationToken));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory("PatchServiceTests.cs", 9)]
    [InlineData([false])]
    [InlineData([true])]
    public async Task PatchRoundTripFindsNamedResourceAfterIndexShift(bool includesRoomOrder)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string originalPath = Path.Combine(directory, "original.win");
            string modifiedPath = Path.Combine(directory, "modified.win");
            string patchPath = Path.Combine(directory, "change.g3mpatch");
            string outputPath = Path.Combine(directory, "output.win");
            string reportPath = Path.Combine(directory, "round-trip.md");
            using (GameMakerData original = GameMakerData.CreateNew2026LTS())
            {
                original.Rooms.Add(new GameMakerRoom
                {
                    Name = original.Strings.MakeString("room_target")
                });
                Write(originalPath, original);
            }
            using (GameMakerData modified = Read(originalPath))
            {
                modified.Rooms.Insert(0, new GameMakerRoom
                {
                    Name = modified.Strings.MakeString("room_inserted")
                });
                modified.Rooms.ByName("room_target").Width = 1234u;
                if (includesRoomOrder)
                {
                    ((Collection<GameMakerResourceById<GameMakerRoom, GameMakerChunkROOM>>)(object)modified.GeneralInfo.RoomOrder).Add(new GameMakerResourceById<GameMakerRoom, GameMakerChunkROOM>
                    {
                        Resource = modified.Rooms.ByName("room_inserted")
                    });
                    ((Collection<GameMakerResourceById<GameMakerRoom, GameMakerChunkROOM>>)(object)modified.GeneralInfo.RoomOrder).Add(new GameMakerResourceById<GameMakerRoom, GameMakerChunkROOM>
                    {
                        Resource = modified.Rooms.ByName("room_target")
                    });
                }
                Write(modifiedPath, modified);
            }
            Assert.True((await PatchService.CreatePatchAsync(originalPath, modifiedPath, patchPath)).Success);
            Assert.True((await PatchService.ValidatePatchAsync(patchPath, originalPath)).Success);
            Assert.True((await PatchService.ApplyPatchAsync(originalPath, patchPath, outputPath)).Success);
            DiffResult diff = await DiffService.CompareAsync(outputPath, modifiedPath, reportPath);
            Assert.True(diff.Success, diff.Error);
            bool condition = diff.DifferenceCount == 0;
            Assert.True(condition, await File.ReadAllTextAsync(reportPath, TestContext.Current.CancellationToken));
            using GameMakerData output = Read(outputPath);
            Assert.Equal(1234u, output.Rooms.ByName("room_target").Width);
            Assert.NotNull(output.Rooms.ByName("room_inserted"));
            IEnumerable<string> expected;
            if (!includesRoomOrder)
            {
                IEnumerable<string> enumerable = Array.Empty<string>();
                expected = enumerable;
            }
            else
            {
                IEnumerable<string> enumerable = ["room_inserted", "room_target"];
                expected = enumerable;
            }
            Assert.Equal<string>(expected, ((IEnumerable<GameMakerResourceById<GameMakerRoom, GameMakerChunkROOM>>)output.GeneralInfo.RoomOrder).Select(room => room.Resource?.Name?.Content ?? string.Empty));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact("PatchServiceTests.cs", 69)]
    public async Task MergeAppliesIndependentRoomChanges()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string originalPath = Path.Combine(directory, "original.win");
            string firstPath = Path.Combine(directory, "first.win");
            string secondPath = Path.Combine(directory, "second.win");
            string firstPatch = Path.Combine(directory, "first.g3mpatch");
            string secondPatch = Path.Combine(directory, "second.g3mpatch");
            string outputPath = Path.Combine(directory, "merged.win");
            string expectedPath = Path.Combine(directory, "expected.win");
            string reportPath = Path.Combine(directory, "merge.md");
            using (GameMakerData original = GameMakerData.CreateNew2026LTS())
            {
                original.Rooms.Add(new GameMakerRoom
                {
                    Name = original.Strings.MakeString("room_first")
                });
                original.Rooms.Add(new GameMakerRoom
                {
                    Name = original.Strings.MakeString("room_second")
                });
                Write(originalPath, original);
            }
            using (GameMakerData first = Read(originalPath))
            {
                first.Rooms.ByName("room_first").Width = 111u;
                Write(firstPath, first);
            }
            using (GameMakerData second = Read(originalPath))
            {
                second.Rooms.ByName("room_second").Height = 222u;
                Write(secondPath, second);
            }
            using (GameMakerData expected = Read(originalPath))
            {
                expected.Rooms.ByName("room_first").Width = 111u;
                expected.Rooms.ByName("room_second").Height = 222u;
                Write(expectedPath, expected);
            }
            Assert.True((await PatchService.CreatePatchAsync(originalPath, firstPath, firstPatch)).Success);
            Assert.True((await PatchService.CreatePatchAsync(originalPath, secondPath, secondPatch)).Success);
            Assert.True((await MergeService.MergePatchesAsync(originalPath, [firstPatch, secondPatch], new MergeOptions
            {
                ApplyPath = outputPath
            })).Success);
            DiffResult diff = await DiffService.CompareAsync(outputPath, expectedPath, reportPath);
            Assert.True(diff.Success, diff.Error);
            bool condition = diff.DifferenceCount == 0;
            Assert.True(condition, await File.ReadAllTextAsync(reportPath, TestContext.Current.CancellationToken));
            using GameMakerData output = Read(outputPath);
            Assert.Equal(111u, output.Rooms.ByName("room_first").Width);
            Assert.Equal(222u, output.Rooms.ByName("room_second").Height);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact("PatchServiceTests.cs", 126)]
    public async Task SequentialMergeKeepsChangesWhenBothPatchesShiftIndices()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string originalPath = Path.Combine(directory, "original.win");
            string firstPath = Path.Combine(directory, "first.win");
            string secondPath = Path.Combine(directory, "second.win");
            string firstPatch = Path.Combine(directory, "first.g3mpatch");
            string secondPatch = Path.Combine(directory, "second.g3mpatch");
            Path.Combine(directory, "first-applied.win");
            string outputPath = Path.Combine(directory, "merged.win");
            using (GameMakerData original = GameMakerData.CreateNew2026LTS())
            {
                string[] array = ["room_a", "room_b", "room_c"];
                foreach (string name in array)
                {
                    original.Rooms.Add(new GameMakerRoom
                    {
                        Name = original.Strings.MakeString(name)
                    });
                }
                Write(originalPath, original);
            }
            using (GameMakerData first = Read(originalPath))
            {
                first.Rooms.Insert(0, new GameMakerRoom
                {
                    Name = first.Strings.MakeString("room_first_inserted")
                });
                first.Rooms.ByName("room_a").Width = 101u;
                Write(firstPath, first);
            }
            using (GameMakerData second = Read(originalPath))
            {
                second.Rooms.Insert(0, new GameMakerRoom
                {
                    Name = second.Strings.MakeString("room_second_inserted")
                });
                second.Rooms.ByName("room_c").Height = 303u;
                Write(secondPath, second);
            }
            Assert.True((await PatchService.CreatePatchAsync(originalPath, firstPath, firstPatch)).Success);
            Assert.True((await PatchService.CreatePatchAsync(originalPath, secondPath, secondPatch)).Success);
            Assert.True((await MergeService.MergePatchesAsync(originalPath, [firstPatch, secondPatch], new MergeOptions
            {
                ApplyPath = outputPath,
                UseSequentialMerge = true
            })).Success);
            using GameMakerData output = Read(outputPath);
            Assert.Equal(101u, output.Rooms.ByName("room_a").Width);
            Assert.Equal(303u, output.Rooms.ByName("room_c").Height);
            Assert.NotNull(output.Rooms.ByName("room_first_inserted"));
            Assert.NotNull(output.Rooms.ByName("room_second_inserted"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact("PatchServiceTests.cs", 177)]
    public async Task MergeKeepsIndependentChangesAfterMultipleResourceIndexShifts()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string originalPath = Path.Combine(directory, "original.win");
            string firstPath = Path.Combine(directory, "first.win");
            string secondPath = Path.Combine(directory, "second.win");
            string firstPatch = Path.Combine(directory, "first.g3mpatch");
            string secondPatch = Path.Combine(directory, "second.g3mpatch");
            string outputPath = Path.Combine(directory, "merged.win");
            using (GameMakerData original = GameMakerData.CreateNew2026LTS())
            {
                string[] array = ["room_a", "room_b", "room_c", "room_d", "room_e", "room_f", "room_g"];
                foreach (string name in array)
                {
                    original.Rooms.Add(new GameMakerRoom
                    {
                        Name = original.Strings.MakeString(name)
                    });
                }
                Write(originalPath, original);
            }
            using (GameMakerData first = Read(originalPath))
            {
                first.Rooms.Insert(0, new GameMakerRoom
                {
                    Name = first.Strings.MakeString("room_first_0")
                });
                first.Rooms.Insert(1, new GameMakerRoom
                {
                    Name = first.Strings.MakeString("room_first_1")
                });
                first.Rooms.Remove(first.Rooms.ByName("room_f"));
                first.Rooms.ByName("room_c").Width = 123u;
                Write(firstPath, first);
            }
            using (GameMakerData second = Read(originalPath))
            {
                second.Rooms.Insert(2, new GameMakerRoom
                {
                    Name = second.Strings.MakeString("room_second")
                });
                second.Rooms.Remove(second.Rooms.ByName("room_b"));
                second.Rooms.ByName("room_g").Height = 456u;
                Write(secondPath, second);
            }
            Assert.True((await PatchService.CreatePatchAsync(originalPath, firstPath, firstPatch)).Success);
            Assert.True((await PatchService.CreatePatchAsync(originalPath, secondPath, secondPatch)).Success);
            Assert.True((await MergeService.MergePatchesAsync(originalPath, [firstPatch, secondPatch], new MergeOptions
            {
                ApplyPath = outputPath
            })).Success);
            using GameMakerData output = Read(outputPath);
            Assert.Equal(123u, output.Rooms.ByName("room_c").Width);
            Assert.Equal(456u, output.Rooms.ByName("room_g").Height);
            Assert.NotNull(output.Rooms.ByName("room_first_0"));
            Assert.NotNull(output.Rooms.ByName("room_first_1"));
            Assert.NotNull(output.Rooms.ByName("room_second"));
            Assert.Null(output.Rooms.ByName("room_b"));
            Assert.Null(output.Rooms.ByName("room_f"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact("PatchServiceTests.cs", 233)]
    public async Task MergePreservesGameObjectReferencesAfterIndexShifts()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string originalPath = Path.Combine(directory, "original.win");
            string firstPath = Path.Combine(directory, "first.win");
            string secondPath = Path.Combine(directory, "second.win");
            string firstPatch = Path.Combine(directory, "first.g3mpatch");
            string secondPatch = Path.Combine(directory, "second.g3mpatch");
            string firstAppliedPath = Path.Combine(directory, "first-applied.win");
            string outputPath = Path.Combine(directory, "merged.win");
            using (GameMakerData original = GameMakerData.CreateNew2026LTS())
            {
                GameMakerGameObject parent = new()
                {
                    Name = original.Strings.MakeString("obj_parent")
                };
                original.GameObjects.Add(parent);
                original.GameObjects.Add(new GameMakerGameObject
                {
                    Name = original.Strings.MakeString("obj_child"),
                    ParentId = parent
                });
                original.GameObjects.Add(new GameMakerGameObject
                {
                    Name = original.Strings.MakeString("obj_target")
                });
                Write(originalPath, original);
            }
            using (GameMakerData first = Read(originalPath))
            {
                first.GameObjects.Insert(0, new GameMakerGameObject
                {
                    Name = first.Strings.MakeString("obj_first")
                });
                first.GameObjects.ByName("obj_child").Depth = 321;
                Write(firstPath, first);
            }
            using (GameMakerData second = Read(originalPath))
            {
                second.GameObjects.Insert(1, new GameMakerGameObject
                {
                    Name = second.Strings.MakeString("obj_second")
                });
                second.GameObjects.ByName("obj_child").Solid = true;
                second.GameObjects.ByName("obj_target").Solid = true;
                Write(secondPath, second);
            }
            Assert.True((await PatchService.CreatePatchAsync(originalPath, firstPath, firstPatch)).Success);
            Assert.True((await PatchService.CreatePatchAsync(originalPath, secondPath, secondPatch)).Success);
            Assert.True((await PatchService.ApplyPatchAsync(originalPath, firstPatch, firstAppliedPath)).Success);
            using (GameMakerData firstApplied = Read(firstAppliedPath))
            {
                Assert.Equal(321, firstApplied.GameObjects.ByName("obj_child").Depth);
            }
            Assert.True((await MergeService.MergePatchesAsync(originalPath, [firstPatch, secondPatch], new MergeOptions
            {
                ApplyPath = outputPath
            })).Success);
            using GameMakerData output = Read(outputPath);
            Assert.Equal(321, output.GameObjects.ByName("obj_child").Depth);
            Assert.True(output.GameObjects.ByName("obj_child").Solid);
            Assert.True(output.GameObjects.ByName("obj_target").Solid);
            var parentResource = output.GameObjects.ByName("obj_child").ParentId;
            Assert.NotNull(parentResource);
            Assert.Equal("obj_parent", parentResource!.Name.Content);
            Assert.NotNull(output.GameObjects.ByName("obj_first"));
            Assert.NotNull(output.GameObjects.ByName("obj_second"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact("PatchServiceTests.cs", 296)]
    public async Task MergeCombinesThreePatchesWithIndependentRoomIndexShifts()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string originalPath = Path.Combine(directory, "original.win");
            string firstPath = Path.Combine(directory, "first.win");
            string secondPath = Path.Combine(directory, "second.win");
            string thirdPath = Path.Combine(directory, "third.win");
            string firstPatch = Path.Combine(directory, "first.g3mpatch");
            string secondPatch = Path.Combine(directory, "second.g3mpatch");
            string thirdPatch = Path.Combine(directory, "third.g3mpatch");
            string outputPath = Path.Combine(directory, "merged.win");
            using (GameMakerData original = GameMakerData.CreateNew2026LTS())
            {
                string[] array = ["room_a", "room_b", "room_c", "room_d", "room_e", "room_f"];
                foreach (string name in array)
                {
                    original.Rooms.Add(new GameMakerRoom
                    {
                        Name = original.Strings.MakeString(name)
                    });
                }
                Write(originalPath, original);
            }
            using (GameMakerData first = Read(originalPath))
            {
                first.Rooms.Insert(0, new GameMakerRoom
                {
                    Name = first.Strings.MakeString("room_first")
                });
                first.Rooms.ByName("room_a").Width = 101u;
                first.GeneralInfo.LastObj = 101u;
                Write(firstPath, first);
            }
            using (GameMakerData second = Read(originalPath))
            {
                second.Rooms.Insert(3, new GameMakerRoom
                {
                    Name = second.Strings.MakeString("room_second")
                });
                second.Rooms.Remove(second.Rooms.ByName("room_b"));
                second.Rooms.ByName("room_c").Height = 202u;
                second.GeneralInfo.DefaultWindowWidth = 202u;
                Write(secondPath, second);
            }
            using (GameMakerData third = Read(originalPath))
            {
                third.Rooms.Insert(1, new GameMakerRoom
                {
                    Name = third.Strings.MakeString("room_third")
                });
                third.Rooms.Remove(third.Rooms.ByName("room_d"));
                third.Rooms.ByName("room_e").Width = 303u;
                third.GeneralInfo.DefaultWindowHeight = 303u;
                Write(thirdPath, third);
            }
            Assert.True((await PatchService.CreatePatchAsync(originalPath, firstPath, firstPatch)).Success);
            Assert.True((await PatchService.CreatePatchAsync(originalPath, secondPath, secondPatch)).Success);
            Assert.True((await PatchService.CreatePatchAsync(originalPath, thirdPath, thirdPatch)).Success);
            Assert.True((await MergeService.MergePatchesAsync(originalPath, [firstPatch, secondPatch, thirdPatch], new MergeOptions
            {
                ApplyPath = outputPath,
                UsePropertyMerge = true
            })).Success);
            Assert.NotEmpty(Directory.GetFiles(directory, "*.merge_log.md"));
            using GameMakerData output = Read(outputPath);
            Assert.Equal(101u, output.Rooms.ByName("room_a").Width);
            Assert.Equal(202u, output.Rooms.ByName("room_c").Height);
            Assert.Equal(303u, output.Rooms.ByName("room_e").Width);
            Assert.Equal(101u, output.GeneralInfo.LastObj);
            Assert.Equal(202u, output.GeneralInfo.DefaultWindowWidth);
            Assert.Equal(303u, output.GeneralInfo.DefaultWindowHeight);
            Assert.NotNull(output.Rooms.ByName("room_first"));
            Assert.NotNull(output.Rooms.ByName("room_second"));
            Assert.NotNull(output.Rooms.ByName("room_third"));
            Assert.Null(output.Rooms.ByName("room_b"));
            Assert.Null(output.Rooms.ByName("room_d"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact("PatchServiceTests.cs", 368)]
    public async Task MergeKeepsHighIndexRoomsAcrossThreeShiftedPatches()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string originalPath = Path.Combine(directory, "original.win");
            string[] paths =
            [
                Path.Combine(directory, "first.win"),
                Path.Combine(directory, "second.win"),
                Path.Combine(directory, "third.win")
            ];
            string[] patches =
            [
                Path.Combine(directory, "first.g3mpatch"),
                Path.Combine(directory, "second.g3mpatch"),
                Path.Combine(directory, "third.g3mpatch")
            ];
            string outputPath = Path.Combine(directory, "merged.win");
            using (GameMakerData original = GameMakerData.CreateNew2026LTS())
            {
                for (int i = 0; i < 130; i++)
                {
                    original.Rooms.Add(new GameMakerRoom
                    {
                        Name = original.Strings.MakeString($"room_{i:D3}")
                    });
                }
                Write(originalPath, original);
            }
            using (GameMakerData first = Read(originalPath))
            {
                first.Rooms.Insert(0, new GameMakerRoom
                {
                    Name = first.Strings.MakeString("room_first")
                });
                first.Rooms.Remove(first.Rooms.ByName("room_010"));
                first.Rooms.ByName("room_123").Width = 1123u;
                Write(paths[0], first);
            }
            using (GameMakerData second = Read(originalPath))
            {
                second.Rooms.Insert(119, new GameMakerRoom
                {
                    Name = second.Strings.MakeString("room_second")
                });
                second.Rooms.Remove(second.Rooms.ByName("room_020"));
                second.Rooms.ByName("room_124").Height = 2124u;
                Write(paths[1], second);
            }
            using (GameMakerData third = Read(originalPath))
            {
                third.Rooms.Insert(125, new GameMakerRoom
                {
                    Name = third.Strings.MakeString("room_third")
                });
                third.Rooms.Remove(third.Rooms.ByName("room_030"));
                third.Rooms.ByName("room_125").Width = 3125u;
                Write(paths[2], third);
            }
            for (int j = 0; j < patches.Length; j++)
            {
                Assert.True((await PatchService.CreatePatchAsync(originalPath, paths[j], patches[j])).Success);
            }
            Assert.True((await MergeService.MergePatchesAsync(originalPath, [.. patches], new MergeOptions
            {
                ApplyPath = outputPath
            })).Success);
            using GameMakerData output = Read(outputPath);
            Assert.Equal(1123u, output.Rooms.ByName("room_123").Width);
            Assert.Equal(2124u, output.Rooms.ByName("room_124").Height);
            Assert.Equal(3125u, output.Rooms.ByName("room_125").Width);
            Assert.NotNull(output.Rooms.ByName("room_first"));
            Assert.NotNull(output.Rooms.ByName("room_second"));
            Assert.NotNull(output.Rooms.ByName("room_third"));
            Assert.Null(output.Rooms.ByName("room_010"));
            Assert.Null(output.Rooms.ByName("room_020"));
            Assert.Null(output.Rooms.ByName("room_030"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact("PatchServiceTests.cs", 440)]
    public async Task MergeKeepsSixIndependentHighIndexChangesAfterDivergentRoomShifts()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string originalPath = Path.Combine(directory, "original.win");
            List<string> patchPaths = [];
            int[] insertionIndexes = [0, 24, 48, 72, 96, 120];
            using (GameMakerData original = GameMakerData.CreateNew2026LTS())
            {
                for (int i = 0; i < 140; i++)
                {
                    original.Rooms.Add(new GameMakerRoom
                    {
                        Name = original.Strings.MakeString($"room_{i:D3}")
                    });
                }
                Write(originalPath, original);
            }
            for (int j = 0; j < insertionIndexes.Length; j++)
            {
                string modifiedPath = Path.Combine(directory, $"mod_{j}.win");
                string patchPath = Path.Combine(directory, $"mod_{j}.g3mpatch");
                using (GameMakerData modified = Read(originalPath))
                {
                    modified.Rooms.Insert(insertionIndexes[j], new GameMakerRoom
                    {
                        Name = modified.Strings.MakeString($"mod_room_{j}")
                    });
                    modified.Rooms.Remove(modified.Rooms.ByName($"room_{j + 10:D3}"));
                    modified.Rooms.ByName($"room_{120 + j:D3}").Width = (uint)(5000 + j);
                    Write(modifiedPath, modified);
                }
                Assert.True((await PatchService.CreatePatchAsync(originalPath, modifiedPath, patchPath)).Success);
                patchPaths.Add(patchPath);
            }
            string outputPath = Path.Combine(directory, "merged.win");
            Assert.True((await MergeService.MergePatchesAsync(originalPath, patchPaths, new MergeOptions
            {
                ApplyPath = outputPath,
                UsePropertyMerge = true
            })).Success);
            using GameMakerData output = Read(outputPath);
            for (int k = 0; k < insertionIndexes.Length; k++)
            {
                Assert.NotNull(output.Rooms.ByName($"mod_room_{k}"));
                Assert.Null(output.Rooms.ByName($"room_{k + 10:D3}"));
                Assert.Equal((uint)(5000 + k), output.Rooms.ByName($"room_{120 + k:D3}").Width);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static GameMakerData Read(string source)
    {
        using FileStream stream = File.OpenRead(source);
        return GameMakerIO.Read(stream);
    }

    private static void Write(string path, GameMakerData data)
    {
        using FileStream stream = File.Create(path);
        GameMakerIO.Write(stream, data);
    }
}
