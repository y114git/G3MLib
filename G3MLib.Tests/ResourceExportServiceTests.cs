using System;
using System.IO;
using System.Text.Json;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.Modding.Resources;
using Xunit;

namespace G3MLib.Tests;

public class ResourceExportServiceTests
{
    [Fact("ResourceExportServiceTests.cs", 10)]
    public void ExportPreservesReferenceIndexes()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using GameMakerData data = GameMakerData.CreateNew2026LTS();
            GameMakerTexturePageItem texture = new()
            {
                Name = data.Strings.MakeString("page_item")
            };
            data.TexturePageItems.Add(new GameMakerTexturePageItem
            {
                Name = data.Strings.MakeString("unused")
            });
            data.TexturePageItems.Add(texture);
            data.EmbeddedImages.Add(new GameMakerEmbeddedImage
            {
                Name = data.Strings.MakeString("embedded"),
                TextureEntry = texture
            });
            data.GameObjects.Add(new GameMakerGameObject
            {
                Name = data.Strings.MakeString("first")
            });
            data.GameObjects.Add(new GameMakerGameObject
            {
                Name = data.Strings.MakeString("second")
            });
            ResourceExportService.ExportEmbeddedImages(data, directory);
            ResourceExportService.ExportGameObjects(data, directory);
            using JsonDocument imageJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "EmbeddedImages", "embedded", "embedded.json")));
            using JsonDocument objectJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "GameObjects", "second__idx0001", "object.json")));
            Assert.Equal(1, imageJson.RootElement.GetProperty("texturePageItemIndex").GetInt32());
            Assert.Equal(1, objectJson.RootElement.GetProperty("index").GetInt32());
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
