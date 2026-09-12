using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Project;
using Xunit;

namespace G3MLib.Tests;

public sealed class ProjectContextTests
{
    [Fact]
    public void ScriptOutputUsesHostCallbacksAndCannotLaunchApplications()
    {
        ProjectContext context = ProjectContext.CreateWithDataFilePaths("input.win", "output.win", "project.json");
        var messages = new List<string>();
        var warnings = new List<string>();
        context.ScriptMessageHandler = messages.Add;
        context.ScriptWarningHandler = warnings.Add;
        context.ScriptMessage("message");
        context.SetUMTConsoleText("console");
        context.SimpleTextOutput("title", "label", "text", false);
        context.ScriptWarning("warning");
        Assert.Equal(new[] { "message", "console", "text" }, messages);
        Assert.Equal(new[] { "warning" }, warnings);
        Assert.Throws<G3MLib.DataFile.Scripting.ScriptException>(() => context.ScriptOpenURL("https://example.com"));
    }

    [Fact("ProjectContextTests.cs", 12)]
    public void ContextFactoriesNormalizeRelativePaths()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");
        string mainFilePath = Path.Combine(directory, "project.json");
        string loadPath = Path.Combine(directory, "input.win");
        string savePath = Path.Combine(directory, "output.win");
        ProjectContext context = ProjectContext.CreateWithDataFilePaths(Path.GetRelativePath(Environment.CurrentDirectory, loadPath), Path.GetRelativePath(Environment.CurrentDirectory, savePath), Path.GetRelativePath(Environment.CurrentDirectory, mainFilePath));
        Assert.Equal(Path.GetFullPath(loadPath), context.LoadDataPath);
        Assert.Equal(Path.GetFullPath(savePath), context.SaveDataPath);
        Assert.Equal(Path.GetFullPath(mainFilePath), context.MainFilePath);
        ProjectContext directoryContext = ProjectContext.CreateWithDirectories(Path.GetRelativePath(Environment.CurrentDirectory, directory), Path.GetRelativePath(Environment.CurrentDirectory, directory), Path.GetRelativePath(Environment.CurrentDirectory, mainFilePath));
        Assert.Equal(Path.GetFullPath(directory), directoryContext.LoadDirectory);
        Assert.Equal(Path.GetFullPath(mainFilePath), directoryContext.MainFilePath);
    }

    [Fact("ProjectContextTests.cs", 38)]
    public void ProjectPathAndFileOperationJsonConvertersRoundTripCompactForms()
    {
        ProjectMainOptions.PathList singlePath = JsonSerializer.Deserialize<ProjectMainOptions.PathList>("\"data.win\"");
        int num = 1;
        List<ProjectMainOptions.PathList.PathPair> list = new(num);
        CollectionsMarshal.SetCount(list, num);
        CollectionsMarshal.AsSpan(list)[0] = new ProjectMainOptions.PathList.PathPair("data.win", "data.win");
        Assert.Equal<List<ProjectMainOptions.PathList.PathPair>>(list, singlePath.Paths);
        Assert.Equal("\"data.win\"", JsonSerializer.Serialize(singlePath));
        Assert.True(JsonSerializer.Deserialize<ProjectMainOptions.PathList>("null").Empty);
        ProjectMainOptions.PathList objectPath = JsonSerializer.Deserialize<ProjectMainOptions.PathList>("{\"Source\":\"input.win\",\"Destination\":\"output.win\"}");
        num = 1;
        List<ProjectMainOptions.PathList.PathPair> list2 = new(num);
        CollectionsMarshal.SetCount(list2, num);
        CollectionsMarshal.AsSpan(list2)[0] = new ProjectMainOptions.PathList.PathPair("input.win", "output.win");
        Assert.Equal<List<ProjectMainOptions.PathList.PathPair>>(list2, objectPath.Paths);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProjectMainOptions.PathList>("42"));
        ProjectMainOptions.PathList pathList = new();
        num = 2;
        List<ProjectMainOptions.PathList.PathPair> list3 = new(num);
        CollectionsMarshal.SetCount(list3, num);
        Span<ProjectMainOptions.PathList.PathPair> span = CollectionsMarshal.AsSpan(list3);
        span[0] = new ProjectMainOptions.PathList.PathPair("source.win", "destination.win");
        span[1] = new ProjectMainOptions.PathList.PathPair("shared.win", "shared.win");
        pathList.Paths = list3;
        ProjectMainOptions.PathList multiplePaths = pathList;
        ProjectMainOptions.PathList parsedPaths = JsonSerializer.Deserialize<ProjectMainOptions.PathList>(JsonSerializer.Serialize(multiplePaths));
        Assert.Equal(multiplePaths.Paths, parsedPaths.Paths);
        ProjectMainOptions.FileOperation requiredOperation = JsonSerializer.Deserialize<ProjectMainOptions.FileOperation>("\"data.win\"");
        Assert.True(requiredOperation.Required);
        Assert.Equal(singlePath.Paths, requiredOperation.Paths.Paths);
        ProjectMainOptions.FileOperation value = new()
        {
            Paths = multiplePaths,
            Required = false
        };
        ProjectMainOptions.FileOperation parsedOperation = JsonSerializer.Deserialize<ProjectMainOptions.FileOperation>(JsonSerializer.Serialize(value));
        Assert.False(parsedOperation.Required);
        Assert.Equal(multiplePaths.Paths, parsedOperation.Paths.Paths);
        ProjectMainOptions.FileOperation explicitRequiredOperation = JsonSerializer.Deserialize<ProjectMainOptions.FileOperation>("{\"Paths\":\"data.win\",\"Required\":true}");
        Assert.True(explicitRequiredOperation.Required);
        Assert.Equal(singlePath.Paths, explicitRequiredOperation.Paths.Paths);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProjectMainOptions.FileOperation>("{}"));
    }

    [Fact("ProjectContextTests.cs", 71)]
    public void ProjectRoundTripPreservesRoom()
    {
        string projectDirectory = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");
        string mainFilePath = Path.Combine(projectDirectory, "project.json");
        try
        {
            using GameMakerData source = GameMakerData.CreateNew2026LTS();
            GameMakerRoom sourceRoom = source.Rooms.ByName("room0");
            sourceRoom.Width = 640u;
            sourceRoom.Height = 480u;
            sourceRoom.Speed = 60u;
            ProjectContext projectContext = new(source, "input.win", "output.win", mainFilePath, "Test");
            Assert.True(projectContext.MarkAssetForExport(sourceRoom));
            projectContext.Export(clearMarkedAssets: true);
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(projectDirectory, "rooms", "room0.json"))))
            {
                Assert.Equal(640u, document.RootElement.GetProperty("Width").GetUInt32());
            }
            using GameMakerData destination = GameMakerData.CreateNew2026LTS();
            ProjectContext.CreateWithDirectories(projectDirectory, projectDirectory, mainFilePath).Import(destination, new GameFileNoOpBackup());
            Assert.Single(destination.Rooms);
            GameMakerRoom importedRoom = destination.Rooms.ByName("room0");
            Assert.Equal(640u, importedRoom.Width);
            Assert.Equal(480u, importedRoom.Height);
            Assert.Equal(60u, importedRoom.Speed);
        }
        finally
        {
            if (Directory.Exists(projectDirectory))
            {
                Directory.Delete(projectDirectory, recursive: true);
            }
        }
    }

    [Fact("ProjectContextTests.cs", 113)]
    public void ProjectRoundTripPreservesSprite()
    {
        string projectDirectory = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");
        string mainFilePath = Path.Combine(projectDirectory, "project.json");
        try
        {
            using FileStream input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2026.win"));
            using GameMakerData source = GameMakerIO.Read(input);
            GameMakerSprite sourceSprite = source.Sprites.First(sprite => sprite != null);
            string sourceName = sourceSprite.Name.Content;
            uint sourceWidth = sourceSprite.Width;
            uint sourceHeight = sourceSprite.Height;
            ProjectContext projectContext = new(source, "input.win", "output.win", mainFilePath, "Test");
            Assert.True(projectContext.MarkAssetForExport(sourceSprite));
            projectContext.Export(clearMarkedAssets: true);
            using GameMakerData destination = GameMakerData.CreateNew2026LTS();
            ProjectContext.CreateWithDirectories(projectDirectory, projectDirectory, mainFilePath).Import(destination, new GameFileNoOpBackup());
            GameMakerSprite importedSprite = destination.Sprites.ByName(sourceName);
            Assert.Equal(sourceWidth, importedSprite.Width);
            Assert.Equal(sourceHeight, importedSprite.Height);
            Assert.Equal(((Collection<GameMakerSprite.TextureEntry>)(object)sourceSprite.Textures).Count, ((Collection<GameMakerSprite.TextureEntry>)(object)importedSprite.Textures).Count);
        }
        finally
        {
            if (Directory.Exists(projectDirectory))
            {
                Directory.Delete(projectDirectory, recursive: true);
            }
        }
    }

    [Theory("ProjectContextTests.cs", 150)]
    [MemberData(nameof(GameMakerIoTests.DataFixtures), MemberType = typeof(GameMakerIoTests))]
    public void ProjectRoundTripImportsAllExportableAssets(string fileName)
    {
        string projectDirectory = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");
        string mainFilePath = Path.Combine(projectDirectory, "project.json");
        List<IProjectAsset> list = [];
        try
        {
            using FileStream input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
            using GameMakerData source = GameMakerIO.Read(input);
            AddAssets(source.AnimationCurves);
            AddAssets(source.Backgrounds);
            AddAssets(source.Code);
            AddAssets(source.Fonts);
            AddAssets(source.GameObjects);
            AddAssets(source.ParticleSystems);
            AddAssets(source.Paths);
            AddAssets(source.Rooms);
            AddAssets(source.Scripts);
            AddAssets(source.Sequences);
            AddAssets(source.Shaders);
            AddAssets(source.Sounds);
            AddAssets(source.Sprites);
            IProjectAsset[] assets = [.. list];
            assets = [.. assets.Where(asset => asset?.ProjectExportable ?? false)];
            Assert.NotEmpty(assets);
            ProjectContext exporter = new(source, "input.win", "output.win", mainFilePath, "Test");
            Assert.All(assets, delegate (IProjectAsset asset)
            {
                Assert.True(exporter.MarkAssetForExport(asset));
            });
            exporter.Export(clearMarkedAssets: true);
            GameMakerData destination = GameMakerData.CreateNew2026LTS();
            try
            {
                ProjectContext.CreateWithDirectories(projectDirectory, projectDirectory, mainFilePath).Import(destination, new GameFileNoOpBackup());
                Assert.All(assets.Where(asset => asset is not GameMakerCode), delegate (IProjectAsset asset)
                {
                    Assert.NotNull(destination.ByName(asset.ProjectName));
                });
                if (source.ParticleSystems is { Count: > 0 })
                {
                    Assert.Equal(source.ParticleSystems.Count, destination.ParticleSystems.Count);
                    GameMakerParticleSystem sourceSystem = source.ParticleSystems.First(system => system != null);
                    GameMakerParticleSystem importedSystem = destination.ParticleSystems.ByName(sourceSystem.Name.Content);
                    Assert.Equal(sourceSystem.OriginX, importedSystem.OriginX);
                    Assert.Equal(sourceSystem.OriginY, importedSystem.OriginY);
                    Assert.Equal(sourceSystem.DrawOrder, importedSystem.DrawOrder);
                    Assert.Equal(((Collection<GameMakerResourceById<GameMakerParticleSystemEmitter, GameMakerChunkPSEM>>)(object)sourceSystem.Emitters).Count, ((Collection<GameMakerResourceById<GameMakerParticleSystemEmitter, GameMakerChunkPSEM>>)(object)importedSystem.Emitters).Count);
                    GameMakerParticleSystemEmitter sourceEmitter = ((IEnumerable<GameMakerResourceById<GameMakerParticleSystemEmitter, GameMakerChunkPSEM>>)sourceSystem.Emitters).FirstOrDefault(reference => reference?.Resource != null)?.Resource;
                    if (sourceEmitter != null)
                    {
                        GameMakerParticleSystemEmitter importedEmitter = destination.ParticleSystemEmitters.ByName(sourceEmitter.Name.Content);
                        Assert.Equal(sourceEmitter.RegionWidth, importedEmitter.RegionWidth);
                        Assert.Equal(sourceEmitter.StartColor, importedEmitter.StartColor);
                        Assert.Equal(sourceEmitter.Sprite?.Name?.Content, importedEmitter.Sprite?.Name?.Content);
                    }
                }
            }
            finally
            {
                if (destination != null)
                {
                    ((IDisposable)destination).Dispose();
                }
            }
        }
        finally
        {
            if (Directory.Exists(projectDirectory))
            {
                Directory.Delete(projectDirectory, recursive: true);
            }
        }

        void AddAssets(IEnumerable<IProjectAsset>? values)
        {
            if (values is not null)
            {
                list.AddRange(values);
            }
        }
    }
}
