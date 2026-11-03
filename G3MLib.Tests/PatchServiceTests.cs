using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using G3MLib.Modding.Hashing;
using G3MLib.Modding.Resources;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.Modding.Diffing;
using G3MLib.Modding.Merging;
using G3MLib.Modding.Patching;

namespace G3MLib.Tests;

public class PatchServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PatchManifestsRecordLibraryVersionAndAcceptLegacyInputs(bool sequential)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var original = Path.Combine(directory, "original.win");
            var modified = Path.Combine(directory, "modified.win");
            var patch = Path.Combine(directory, "change.g3mpatch");
            var merged = Path.Combine(directory, "merged.g3mpatch");
            using (var data = GameMakerData.CreateNew2026LTS())
            {
                Write(original, data);
                data.Rooms[0].Width++;
                Write(modified, data);
            }
            var created = await PatchService.CreatePatchAsync(original, modified, patch, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(created.Success, created.Error);
            using (var archive = System.IO.Compression.ZipFile.Open(patch, System.IO.Compression.ZipArchiveMode.Update))
            {
                var entry = archive.GetEntry("g3mpatch.json");
                System.Text.Json.Nodes.JsonObject manifest;
                using (var stream = entry.Open())
                    manifest = System.Text.Json.Nodes.JsonNode.Parse(stream).AsObject();
                Assert.Equal("G3MLib", manifest["library"]["name"].GetValue<string>());
                Assert.Equal(Modding.ModdingMetadata.LibraryVersion, manifest["library"]["version"].GetValue<string>());
                manifest.Remove("library");
                Assert.Null(System.Text.Json.JsonSerializer.Deserialize<G3MPatchManifest>(manifest.ToJsonString()).Library);
                entry.Delete();
                using var output = archive.CreateEntry("g3mpatch.json").Open();
                System.Text.Json.JsonSerializer.Serialize(output, manifest);
            }
            var applied = await PatchService.ApplyPatchAsync(original, patch, Path.Combine(directory, "applied.win"), cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(applied.Success, applied.Error);
            var result = await MergeService.MergePatchesAsync(original, [patch, patch], new MergeOptions
            {
                OutputPath = merged,
                UseSequentialMerge = sequential
            }, TestContext.Current.CancellationToken);
            Assert.True(result.Success, result.Error);
            using var mergedArchive = System.IO.Compression.ZipFile.OpenRead(merged);
            using var manifestStream = mergedArchive.GetEntry("g3mpatch.json").Open();
            var mergedManifest = System.Text.Json.JsonSerializer.Deserialize<G3MPatchManifest>(manifestStream);
            Assert.Equal("G3MLib", mergedManifest.Library.Name);
            Assert.Equal(Modding.ModdingMetadata.LibraryVersion, mergedManifest.Library.Version);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SequentialMergeRejectsPropertyMerging()
    {
        var result = await MergeService.MergePatchesAsync("unused.win", [], new MergeOptions { UseSequentialMerge = true, UsePropertyMerge = true }, TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal("Sequential merge does not support code or property merging.", result.Error);
    }

    [Fact]
    public async Task MergeDataInputsSupportsOverlayWithoutManifest()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var original = Path.Combine(directory, "original.win");
            var modified = Path.Combine(directory, "modified.win");
            var output = Path.Combine(directory, "merged.win");
            using (var data = GameMakerData.CreateNew2026LTS())
            {
                Write(original, data);
                data.GeneralInfo.LastObj = 321;
                Write(modified, data);
            }

            var result = await MergeService.MergePatchesAsync(original, [modified, modified], new MergeOptions { ApplyPath = output }, TestContext.Current.CancellationToken);

            Assert.True(result.Success, result.Error);
            using var merged = Read(output);
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
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var original = Path.Combine(directory, "original.win");
            var patch = Path.Combine(directory, "invalid.g3mpatch");
            using (var data = GameMakerData.CreateNew2026LTS())
                Write(original, data);
            var expected = await File.ReadAllBytesAsync(original, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(patch, "invalid zip", TestContext.Current.CancellationToken);

            var result = await PatchService.ApplyPatchAsync(original, patch, original, cancellationToken: TestContext.Current.CancellationToken);

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
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var originalPath = Path.Combine(directory, "original.win");
            var modifiedPath = Path.Combine(directory, "modified.win");
            var patchPath = Path.Combine(directory, "change.g3mpatch");
            var outputPath = Path.Combine(directory, "output.win");
            var reportPath = Path.Combine(directory, "round-trip.md");
            using (var original = GameMakerData.CreateNew2026LTS())
            {
                original.Rooms.Add(new GameMakerRoom
                {
                    Name = original.Strings.MakeString("room_target")
                });
                Write(originalPath, original);
            }
            using (var modified = Read(originalPath))
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
            Assert.True((await PatchService.CreatePatchAsync(originalPath, modifiedPath, patchPath, cancellationToken: TestContext.Current.CancellationToken)).Success);
            Assert.True((await PatchService.ValidatePatchAsync(patchPath, originalPath, cancellationToken: TestContext.Current.CancellationToken)).Success);
            Assert.True((await PatchService.ApplyPatchAsync(originalPath, patchPath, outputPath, cancellationToken: TestContext.Current.CancellationToken)).Success);
            var diff = await DiffService.CompareAsync(outputPath, modifiedPath, reportPath, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(diff.Success, diff.Error);
            var condition = diff.DifferenceCount == 0;
            Assert.True(condition, await File.ReadAllTextAsync(reportPath, TestContext.Current.CancellationToken));
            using var output = Read(outputPath);
            Assert.Equal(1234u, output.Rooms.ByName("room_target").Width);
            Assert.NotNull(output.Rooms.ByName("room_inserted"));
            IEnumerable<string> expected;
            if (!includesRoomOrder)
            {
                IEnumerable<string> enumerable = [];
                expected = enumerable;
            }
            else
            {
                IEnumerable<string> enumerable = ["room_inserted", "room_target"];
                expected = enumerable;
            }
            Assert.Equal(expected, output.GeneralInfo.RoomOrder.Select(room => room.Resource?.Name?.Content ?? string.Empty));
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
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var originalPath = Path.Combine(directory, "original.win");
            var firstPath = Path.Combine(directory, "first.win");
            var secondPath = Path.Combine(directory, "second.win");
            var firstPatch = Path.Combine(directory, "first.g3mpatch");
            var secondPatch = Path.Combine(directory, "second.g3mpatch");
            var outputPath = Path.Combine(directory, "merged.win");
            var expectedPath = Path.Combine(directory, "expected.win");
            var reportPath = Path.Combine(directory, "merge.md");
            using (var original = GameMakerData.CreateNew2026LTS())
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
            using (var first = Read(originalPath))
            {
                first.Rooms.ByName("room_first").Width = 111u;
                Write(firstPath, first);
            }
            using (var second = Read(originalPath))
            {
                second.Rooms.ByName("room_second").Height = 222u;
                Write(secondPath, second);
            }
            using (var expected = Read(originalPath))
            {
                expected.Rooms.ByName("room_first").Width = 111u;
                expected.Rooms.ByName("room_second").Height = 222u;
                Write(expectedPath, expected);
            }
            Assert.True((await PatchService.CreatePatchAsync(originalPath, firstPath, firstPatch, cancellationToken: TestContext.Current.CancellationToken)).Success);
            Assert.True((await PatchService.CreatePatchAsync(originalPath, secondPath, secondPatch, cancellationToken: TestContext.Current.CancellationToken)).Success);
            Assert.True((await MergeService.MergePatchesAsync(originalPath, [firstPatch, secondPatch], new MergeOptions
            {
                ApplyPath = outputPath
            }, TestContext.Current.CancellationToken)).Success);
            var diff = await DiffService.CompareAsync(outputPath, expectedPath, reportPath, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(diff.Success, diff.Error);
            var condition = diff.DifferenceCount == 0;
            Assert.True(condition, await File.ReadAllTextAsync(reportPath, TestContext.Current.CancellationToken));
            using var output = Read(outputPath);
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
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var originalPath = Path.Combine(directory, "original.win");
            var firstPath = Path.Combine(directory, "first.win");
            var secondPath = Path.Combine(directory, "second.win");
            var firstPatch = Path.Combine(directory, "first.g3mpatch");
            var secondPatch = Path.Combine(directory, "second.g3mpatch");
            Path.Combine(directory, "first-applied.win");
            var outputPath = Path.Combine(directory, "merged.win");
            using (var original = GameMakerData.CreateNew2026LTS())
            {
                string[] array = ["room_a", "room_b", "room_c"];
                foreach (var name in array)
                {
                    original.Rooms.Add(new GameMakerRoom
                    {
                        Name = original.Strings.MakeString(name)
                    });
                }
                Write(originalPath, original);
            }
            using (var first = Read(originalPath))
            {
                first.Rooms.Insert(0, new GameMakerRoom
                {
                    Name = first.Strings.MakeString("room_first_inserted")
                });
                first.Rooms.ByName("room_a").Width = 101u;
                Write(firstPath, first);
            }
            using (var second = Read(originalPath))
            {
                second.Rooms.Insert(0, new GameMakerRoom
                {
                    Name = second.Strings.MakeString("room_second_inserted")
                });
                second.Rooms.ByName("room_c").Height = 303u;
                Write(secondPath, second);
            }
            Assert.True((await PatchService.CreatePatchAsync(originalPath, firstPath, firstPatch, cancellationToken: TestContext.Current.CancellationToken)).Success);
            Assert.True((await PatchService.CreatePatchAsync(originalPath, secondPath, secondPatch, cancellationToken: TestContext.Current.CancellationToken)).Success);
            Assert.True((await MergeService.MergePatchesAsync(originalPath, [firstPatch, secondPatch], new MergeOptions
            {
                ApplyPath = outputPath,
                UseSequentialMerge = true
            }, TestContext.Current.CancellationToken)).Success);
            using var output = Read(outputPath);
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
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var originalPath = Path.Combine(directory, "original.win");
            var firstPath = Path.Combine(directory, "first.win");
            var secondPath = Path.Combine(directory, "second.win");
            var firstPatch = Path.Combine(directory, "first.g3mpatch");
            var secondPatch = Path.Combine(directory, "second.g3mpatch");
            var outputPath = Path.Combine(directory, "merged.win");
            using (var original = GameMakerData.CreateNew2026LTS())
            {
                string[] array = ["room_a", "room_b", "room_c", "room_d", "room_e", "room_f", "room_g"];
                foreach (var name in array)
                {
                    original.Rooms.Add(new GameMakerRoom
                    {
                        Name = original.Strings.MakeString(name)
                    });
                }
                Write(originalPath, original);
            }
            using (var first = Read(originalPath))
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
            using (var second = Read(originalPath))
            {
                second.Rooms.Insert(2, new GameMakerRoom
                {
                    Name = second.Strings.MakeString("room_second")
                });
                second.Rooms.Remove(second.Rooms.ByName("room_b"));
                second.Rooms.ByName("room_g").Height = 456u;
                Write(secondPath, second);
            }
            Assert.True((await PatchService.CreatePatchAsync(originalPath, firstPath, firstPatch, cancellationToken: TestContext.Current.CancellationToken)).Success);
            Assert.True((await PatchService.CreatePatchAsync(originalPath, secondPath, secondPatch, cancellationToken: TestContext.Current.CancellationToken)).Success);
            Assert.True((await MergeService.MergePatchesAsync(originalPath, [firstPatch, secondPatch], new MergeOptions
            {
                ApplyPath = outputPath
            }, TestContext.Current.CancellationToken)).Success);
            using var output = Read(outputPath);
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
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var originalPath = Path.Combine(directory, "original.win");
            var firstPath = Path.Combine(directory, "first.win");
            var secondPath = Path.Combine(directory, "second.win");
            var firstPatch = Path.Combine(directory, "first.g3mpatch");
            var secondPatch = Path.Combine(directory, "second.g3mpatch");
            var firstAppliedPath = Path.Combine(directory, "first-applied.win");
            var outputPath = Path.Combine(directory, "merged.win");
            using (var original = GameMakerData.CreateNew2026LTS())
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
            using (var first = Read(originalPath))
            {
                first.GameObjects.Insert(0, new GameMakerGameObject
                {
                    Name = first.Strings.MakeString("obj_first")
                });
                first.GameObjects.ByName("obj_child").Depth = 321;
                Write(firstPath, first);
            }
            using (var second = Read(originalPath))
            {
                second.GameObjects.Insert(1, new GameMakerGameObject
                {
                    Name = second.Strings.MakeString("obj_second")
                });
                second.GameObjects.ByName("obj_child").Solid = true;
                second.GameObjects.ByName("obj_target").Solid = true;
                Write(secondPath, second);
            }
            Assert.True((await PatchService.CreatePatchAsync(originalPath, firstPath, firstPatch, cancellationToken: TestContext.Current.CancellationToken)).Success);
            Assert.True((await PatchService.CreatePatchAsync(originalPath, secondPath, secondPatch, cancellationToken: TestContext.Current.CancellationToken)).Success);
            Assert.True((await PatchService.ApplyPatchAsync(originalPath, firstPatch, firstAppliedPath, cancellationToken: TestContext.Current.CancellationToken)).Success);
            using (var firstApplied = Read(firstAppliedPath))
            {
                Assert.Equal(321, firstApplied.GameObjects.ByName("obj_child").Depth);
            }
            Assert.True((await MergeService.MergePatchesAsync(originalPath, [firstPatch, secondPatch], new MergeOptions
            {
                ApplyPath = outputPath
            }, TestContext.Current.CancellationToken)).Success);
            using var output = Read(outputPath);
            Assert.Equal(321, output.GameObjects.ByName("obj_child").Depth);
            Assert.True(output.GameObjects.ByName("obj_child").Solid);
            Assert.True(output.GameObjects.ByName("obj_target").Solid);
            var parentResource = output.GameObjects.ByName("obj_child").ParentId;
            Assert.NotNull(parentResource);
            Assert.Equal("obj_parent", parentResource.Name.Content);
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
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var originalPath = Path.Combine(directory, "original.win");
            var firstPath = Path.Combine(directory, "first.win");
            var secondPath = Path.Combine(directory, "second.win");
            var thirdPath = Path.Combine(directory, "third.win");
            var firstPatch = Path.Combine(directory, "first.g3mpatch");
            var secondPatch = Path.Combine(directory, "second.g3mpatch");
            var thirdPatch = Path.Combine(directory, "third.g3mpatch");
            var outputPath = Path.Combine(directory, "merged.win");
            using (var original = GameMakerData.CreateNew2026LTS())
            {
                string[] array = ["room_a", "room_b", "room_c", "room_d", "room_e", "room_f"];
                foreach (var name in array)
                {
                    original.Rooms.Add(new GameMakerRoom
                    {
                        Name = original.Strings.MakeString(name)
                    });
                }
                Write(originalPath, original);
            }
            using (var first = Read(originalPath))
            {
                first.Rooms.Insert(0, new GameMakerRoom
                {
                    Name = first.Strings.MakeString("room_first")
                });
                first.Rooms.ByName("room_a").Width = 101u;
                first.GeneralInfo.LastObj = 101u;
                Write(firstPath, first);
            }
            using (var second = Read(originalPath))
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
            using (var third = Read(originalPath))
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
            Assert.True((await PatchService.CreatePatchAsync(originalPath, firstPath, firstPatch, cancellationToken: TestContext.Current.CancellationToken)).Success);
            Assert.True((await PatchService.CreatePatchAsync(originalPath, secondPath, secondPatch, cancellationToken: TestContext.Current.CancellationToken)).Success);
            Assert.True((await PatchService.CreatePatchAsync(originalPath, thirdPath, thirdPatch, cancellationToken: TestContext.Current.CancellationToken)).Success);
            Assert.True((await MergeService.MergePatchesAsync(originalPath, [firstPatch, secondPatch, thirdPatch], new MergeOptions
            {
                ApplyPath = outputPath,
                UsePropertyMerge = true
            }, TestContext.Current.CancellationToken)).Success);
            Assert.NotEmpty(Directory.GetFiles(directory, "*.merge_log.md"));
            using var output = Read(outputPath);
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
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var originalPath = Path.Combine(directory, "original.win");
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
            var outputPath = Path.Combine(directory, "merged.win");
            using (var original = GameMakerData.CreateNew2026LTS())
            {
                for (var i = 0; i < 130; i++)
                {
                    original.Rooms.Add(new GameMakerRoom
                    {
                        Name = original.Strings.MakeString($"room_{i:D3}")
                    });
                }
                Write(originalPath, original);
            }
            using (var first = Read(originalPath))
            {
                first.Rooms.Insert(0, new GameMakerRoom
                {
                    Name = first.Strings.MakeString("room_first")
                });
                first.Rooms.Remove(first.Rooms.ByName("room_010"));
                first.Rooms.ByName("room_123").Width = 1123u;
                Write(paths[0], first);
            }
            using (var second = Read(originalPath))
            {
                second.Rooms.Insert(119, new GameMakerRoom
                {
                    Name = second.Strings.MakeString("room_second")
                });
                second.Rooms.Remove(second.Rooms.ByName("room_020"));
                second.Rooms.ByName("room_124").Height = 2124u;
                Write(paths[1], second);
            }
            using (var third = Read(originalPath))
            {
                third.Rooms.Insert(125, new GameMakerRoom
                {
                    Name = third.Strings.MakeString("room_third")
                });
                third.Rooms.Remove(third.Rooms.ByName("room_030"));
                third.Rooms.ByName("room_125").Width = 3125u;
                Write(paths[2], third);
            }
            for (var j = 0; j < patches.Length; j++)
            {
                Assert.True((await PatchService.CreatePatchAsync(originalPath, paths[j], patches[j], cancellationToken: TestContext.Current.CancellationToken)).Success);
            }
            Assert.True((await MergeService.MergePatchesAsync(originalPath, [.. patches], new MergeOptions
            {
                ApplyPath = outputPath
            }, TestContext.Current.CancellationToken)).Success);
            using var output = Read(outputPath);
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
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var originalPath = Path.Combine(directory, "original.win");
            List<string> patchPaths = [];
            int[] insertionIndexes = [0, 24, 48, 72, 96, 120];
            using (var original = GameMakerData.CreateNew2026LTS())
            {
                for (var i = 0; i < 140; i++)
                {
                    original.Rooms.Add(new GameMakerRoom
                    {
                        Name = original.Strings.MakeString($"room_{i:D3}")
                    });
                }
                Write(originalPath, original);
            }
            for (var j = 0; j < insertionIndexes.Length; j++)
            {
                var modifiedPath = Path.Combine(directory, $"mod_{j}.win");
                var patchPath = Path.Combine(directory, $"mod_{j}.g3mpatch");
                using (var modified = Read(originalPath))
                {
                    modified.Rooms.Insert(insertionIndexes[j], new GameMakerRoom
                    {
                        Name = modified.Strings.MakeString($"mod_room_{j}")
                    });
                    modified.Rooms.Remove(modified.Rooms.ByName($"room_{j + 10:D3}"));
                    modified.Rooms.ByName($"room_{120 + j:D3}").Width = (uint)(5000 + j);
                    Write(modifiedPath, modified);
                }
                Assert.True((await PatchService.CreatePatchAsync(originalPath, modifiedPath, patchPath, cancellationToken: TestContext.Current.CancellationToken)).Success);
                patchPaths.Add(patchPath);
            }
            var outputPath = Path.Combine(directory, "merged.win");
            Assert.True((await MergeService.MergePatchesAsync(originalPath, patchPaths, new MergeOptions
            {
                ApplyPath = outputPath,
                UsePropertyMerge = true
            }, TestContext.Current.CancellationToken)).Success);
            using var output = Read(outputPath);
            for (var k = 0; k < insertionIndexes.Length; k++)
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
        using var stream = File.OpenRead(source);
        return GameMakerIO.Read(stream);
    }

    private static byte[] ToBytes(GameMakerData data)
    {
        using var stream = new MemoryStream();
        GameMakerIO.Write(stream, data);
        return stream.ToArray();
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    [Fact]
    public async Task ApplyPatchInMemoryMatchesModifiedDataBytesAndChunkDigests()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var originalPath = Path.Combine(directory, "original.win");
            var modifiedPath = Path.Combine(directory, "modified.win");
            var patchPath = Path.Combine(directory, "change.g3mpatch");

            using (var v1 = GameMakerData.CreateNew2026LTS())
            {
                v1.Rooms.Add(new GameMakerRoom
                {
                    Name = v1.Strings.MakeString("room_alpha"),
                    Width = 640,
                    Height = 480
                });
                v1.GameObjects.Add(new GameMakerGameObject
                {
                    Name = v1.Strings.MakeString("obj_hero"),
                    Depth = 10
                });
                Write(originalPath, v1);
            }

            byte[] expectedBytes;
            using (var v2 = Read(originalPath))
            {
                v2.Rooms.Insert(0, new GameMakerRoom
                {
                    Name = v2.Strings.MakeString("room_intro"),
                    Width = 800,
                    Height = 600
                });
                v2.Rooms.ByName("room_alpha").Width = 1280;
                v2.GameObjects.ByName("obj_hero").Depth = -50;
                v2.GameObjects.ByName("obj_hero").Solid = true;
                v2.GeneralInfo.LastObj = 777;
                Write(modifiedPath, v2);
                expectedBytes = ToBytes(v2);
            }

            var created = await PatchService.CreatePatchAsync(originalPath, modifiedPath, patchPath, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(created.Success, created.Error);

            List<BatchProgress> progressUpdates = [];
            using var inMemoryCopy = Read(originalPath);
            var applyResult = await PatchService.ApplyPatchAsync(
                inMemoryCopy,
                patchPath,
                new PatchApplyOptions { ValidateBeforeApply = true },
                new SynchronousProgress<BatchProgress>(progressUpdates.Add),
                TestContext.Current.CancellationToken);

            Assert.True(applyResult.Success, applyResult.Error);
            Assert.True(applyResult.AppliedCount > 0);
            Assert.Equal(0, applyResult.FailedCount);
            Assert.Empty(applyResult.Warnings);
            Assert.NotEmpty(progressUpdates);
            Assert.Equal(progressUpdates[^1].Total, progressUpdates[^1].Done);

            var actualBytes = ToBytes(inMemoryCopy);
            Assert.Empty(ChunkDigests.Compare(ChunkDigests.Compute(new MemoryStream(actualBytes), token: TestContext.Current.CancellationToken), ChunkDigests.Compute(new MemoryStream(expectedBytes), token: TestContext.Current.CancellationToken)));
            Assert.Equal(expectedBytes, actualBytes);
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
    public async Task ApplyPatchInMemoryAndFileVersionProduceIdenticalResults()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var originalPath = Path.Combine(directory, "original.win");
            var modifiedPath = Path.Combine(directory, "modified.win");
            var patchPath = Path.Combine(directory, "change.g3mpatch");
            var fileAppliedPath = Path.Combine(directory, "file-applied.win");

            using (var v1 = GameMakerData.CreateNew2026LTS())
            {
                v1.Rooms.Add(new GameMakerRoom
                {
                    Name = v1.Strings.MakeString("room_one"),
                    Width = 320,
                    Height = 240
                });
                v1.Rooms.Add(new GameMakerRoom
                {
                    Name = v1.Strings.MakeString("room_two"),
                    Width = 640,
                    Height = 360
                });
                Write(originalPath, v1);
            }

            using (var v2 = Read(originalPath))
            {
                v2.Rooms.Insert(0, new GameMakerRoom
                {
                    Name = v2.Strings.MakeString("room_zero"),
                    Width = 1024,
                    Height = 768
                });
                v2.Rooms.ByName("room_two").Height = 900;
                v2.GeneralInfo.DefaultWindowWidth = 1600;
                Write(modifiedPath, v2);
            }

            Assert.True((await PatchService.CreatePatchAsync(originalPath, modifiedPath, patchPath, cancellationToken: TestContext.Current.CancellationToken)).Success);

            var fileResult = await PatchService.ApplyPatchAsync(originalPath, patchPath, fileAppliedPath, cancellationToken: TestContext.Current.CancellationToken);
            using var inMemoryData = Read(originalPath);
            var inMemoryResult = await PatchService.ApplyPatchAsync(inMemoryData, patchPath, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(fileResult.Success, inMemoryResult.Success);
            Assert.Equal(fileResult.Error, inMemoryResult.Error);
            Assert.Equal(fileResult.AppliedCount, inMemoryResult.AppliedCount);
            Assert.Equal(fileResult.FailedCount, inMemoryResult.FailedCount);
            Assert.Equal(fileResult.Warnings, inMemoryResult.Warnings);

            var fileBytes = await File.ReadAllBytesAsync(fileAppliedPath, TestContext.Current.CancellationToken);
            var memoryBytes = ToBytes(inMemoryData);
            Assert.Empty(ChunkDigests.Compare(ChunkDigests.Compute(new MemoryStream(fileBytes), token: TestContext.Current.CancellationToken), ChunkDigests.Compute(new MemoryStream(memoryBytes), token: TestContext.Current.CancellationToken)));
            Assert.Equal(fileBytes, memoryBytes);

            var minimalModifiedPath = Path.Combine(directory, "minimal-modified.win");
            var minimalPatchPath = Path.Combine(directory, "minimal.g3mpatch");
            var minimalFileOutPath = Path.Combine(directory, "minimal-out.win");
            using (var v2Minimal = Read(originalPath))
            {
                v2Minimal.GeneralInfo.LastObj = 999;
                Write(minimalModifiedPath, v2Minimal);
            }

            Assert.True((await PatchService.CreatePatchAsync(originalPath, minimalModifiedPath, minimalPatchPath, cancellationToken: TestContext.Current.CancellationToken)).Success);

            var minimalFileResult = await PatchService.ApplyPatchAsync(originalPath, minimalPatchPath, minimalFileOutPath, cancellationToken: TestContext.Current.CancellationToken);
            using var minimalMemoryData = Read(originalPath);
            using var patchStream = new MemoryStream(await File.ReadAllBytesAsync(minimalPatchPath, TestContext.Current.CancellationToken));
            var minimalStreamResult = await PatchService.ApplyPatchAsync(minimalMemoryData, patchStream, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(patchStream.CanRead);
            Assert.Equal(minimalFileResult.Success, minimalStreamResult.Success);
            Assert.Equal(minimalFileResult.AppliedCount, minimalStreamResult.AppliedCount);
            Assert.Equal(minimalFileResult.FailedCount, minimalStreamResult.FailedCount);
            Assert.Equal(await File.ReadAllBytesAsync(minimalFileOutPath, TestContext.Current.CancellationToken), ToBytes(minimalMemoryData));
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
    public async Task ApplyPatchInMemoryFromZipEntryStreamWorksWithoutTempFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var originalPath = Path.Combine(directory, "data.win");
            var modifiedPath = Path.Combine(directory, "modified.win");
            var patchPath = Path.Combine(directory, "mod.g3mpatch");
            var modZipPath = Path.Combine(directory, "package.zip");

            using (var original = GameMakerData.CreateNew2026LTS())
            {
                Write(originalPath, original);
                original.Rooms[0].Width = 1920;
                Write(modifiedPath, original);
            }

            Assert.True((await PatchService.CreatePatchAsync(originalPath, modifiedPath, patchPath, cancellationToken: TestContext.Current.CancellationToken)).Success);

            using (var zipStream = File.Create(modZipPath))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(patchPath, "patches/mod.g3mpatch");
            }

            using var modZip = ZipFile.OpenRead(modZipPath);
            using var data = Read(originalPath);
            using var entryStream = modZip.GetEntry("patches/mod.g3mpatch")!.Open();
            var applyResult = await PatchService.ApplyPatchAsync(data, entryStream, null, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(applyResult.Success, applyResult.Error);
            Assert.Equal(1920u, data.Rooms[0].Width);
            Assert.Equal(await File.ReadAllBytesAsync(modifiedPath, TestContext.Current.CancellationToken), ToBytes(data));
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
    public async Task ApplyPatchInMemoryValidateBeforeApplyRejectsBadPatchWithoutMutatingData()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        data.Rooms[0].Width = 777;
        var beforeBytes = ToBytes(data);
        var beforeDigests = ChunkDigests.Compute(new MemoryStream(beforeBytes), token: TestContext.Current.CancellationToken);

        using (var missingManifestStream = new MemoryStream())
        {
            using (var archive = new ZipArchive(missingManifestStream, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(archive.CreateEntry("Rooms/room0/room0.json").Open(), Encoding.UTF8))
            {
                writer.Write("{}");
            }
            missingManifestStream.Position = 0;

            var result1 = await PatchService.ApplyPatchAsync(data, missingManifestStream, new PatchApplyOptions { ValidateBeforeApply = true }, cancellationToken: TestContext.Current.CancellationToken);
            Assert.False(result1.Success);
            Assert.Contains("g3mpatch.json", result1.Error, StringComparison.OrdinalIgnoreCase);
        }

        using (var corruptJsonStream = new MemoryStream())
        {
            using (var archive = new ZipArchive(corruptJsonStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var manifestWriter = new StreamWriter(archive.CreateEntry("g3mpatch.json").Open(), Encoding.UTF8))
                {
                    manifestWriter.Write("""{"patchVersion":1}""");
                }
                using (var roomWriter = new StreamWriter(archive.CreateEntry("Rooms/room0/room0.json").Open(), Encoding.UTF8))
                {
                    roomWriter.Write("{not-valid-json");
                }
            }
            corruptJsonStream.Position = 0;

            var result2 = await PatchService.ApplyPatchAsync(data, corruptJsonStream, new PatchApplyOptions { ValidateBeforeApply = true }, cancellationToken: TestContext.Current.CancellationToken);
            Assert.False(result2.Success);
            Assert.Contains("Invalid patch JSON", result2.Error, StringComparison.OrdinalIgnoreCase);
        }

        var afterBytes = ToBytes(data);
        Assert.Empty(ChunkDigests.Compare(beforeDigests, ChunkDigests.Compute(new MemoryStream(afterBytes), token: TestContext.Current.CancellationToken)));
        Assert.Equal(beforeBytes, afterBytes);
    }

    private sealed class MockOversizedSeekableStream(long length) : MemoryStream
    {
        public override long Length => length;
    }

    [Fact]
    public void PatchFileSystemLoadFromZipThrowsWhenStreamExceedsMaxPayloadBytes()
    {
        using var stream = new MockOversizedSeekableStream(PatchFileSystem.MaxPayloadBytes + 1024);
        var ex = Assert.Throws<InvalidDataException>(() => PatchFileSystem.LoadFromZip(stream));
        Assert.Contains("maximum allowed size", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("EmbeddedTextures/../../escape.png")]
    [InlineData("EmbeddedTextures/C:/escape.png")]
    [InlineData("/rooted/escape.png")]
    [InlineData("EmbeddedTextures\\..\\escape.png")]
    public void PatchArchiveWithAnUnsafeEntryNameIsRefused(string entryName)
    {
        using MemoryStream stream = new();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry(entryName).Open(), Encoding.UTF8);
            writer.Write("x");
        }

        stream.Position = 0;
        var error = Assert.Throws<InvalidDataException>(() => PatchFileSystem.LoadFromZip(stream));
        Assert.Contains("unsafe name", error.Message, StringComparison.Ordinal);
    }

    private static void Write(string path, GameMakerData data)
    {
        using var stream = File.Create(path);
        GameMakerIO.Write(stream, data);
    }
}
