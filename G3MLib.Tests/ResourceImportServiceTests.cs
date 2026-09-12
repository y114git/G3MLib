using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.Modding.Resources;
using Xunit;

namespace G3MLib.Tests;

public class ResourceImportServiceTests
{
    [Fact]
    public async Task PatchContextFlowsAcrossAwaitAndIsolatesChildOperations()
    {
        var parent = new G3MLib.Modding.Patching.PatchFileSystem();
        var child = new G3MLib.Modding.Patching.PatchFileSystem();
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
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "timeline_test"));
        Directory.CreateDirectory(Path.Combine(directory, "extension_test"));
        try
        {
            using GameMakerData data = GameMakerData.CreateNew2026LTS();
            File.WriteAllText(Path.Combine(directory, "timeline_test", "timeline_test.json"), "{\"moments\":[{\"step\":12,\"actions\":[]}]}");
            File.WriteAllText(Path.Combine(directory, "extension_test", "extension_test.json"), "{\"folderName\":\"extensions\",\"version\":\"1.0\",\"files\":[],\"options\":[]}");

            Assert.True(ResourceImportService.Import("Timelines", data, directory));
            Assert.True(ResourceImportService.Import("Extensions", data, directory));

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

    [Fact("ResourceImportServiceTests.cs", 9)]
    public void AssetOrderRewritesCollisionEventIndexes()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using GameMakerData data = GameMakerData.CreateNew2026LTS();
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
            ResourceImportService.ImportAssetOrder(data, directory);
            Assert.Same(target, data.GameObjects[0]);
            Assert.Equal(0u, ((IEnumerable<GameMakerGameObject.Event>)((Collection<GameMakerPointerList<GameMakerGameObject.Event>>)(object)parent.Events)[4]).Single().EventSubtype);
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
