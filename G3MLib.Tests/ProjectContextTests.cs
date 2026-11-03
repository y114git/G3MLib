using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Project;

namespace G3MLib.Tests;

public sealed class ProjectContextTests
{
    [Fact]
    public void ScriptOutputUsesHostCallbacksAndCannotLaunchApplications()
    {
        var context = ProjectContext.CreateWithDataFilePaths("input.win", "output.win", "project.json");
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
        Assert.Throws<DataFile.Scripting.ScriptException>(() => context.ScriptOpenURL("https://example.com"));
    }

    [Fact("ProjectContextTests.cs", 12)]
    public void ContextFactoriesNormalizeRelativePaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");
        var mainFilePath = Path.Combine(directory, "project.json");
        var loadPath = Path.Combine(directory, "input.win");
        var savePath = Path.Combine(directory, "output.win");
        var context = ProjectContext.CreateWithDataFilePaths(Path.GetRelativePath(Environment.CurrentDirectory, loadPath), Path.GetRelativePath(Environment.CurrentDirectory, savePath), Path.GetRelativePath(Environment.CurrentDirectory, mainFilePath));
        Assert.Equal(Path.GetFullPath(loadPath), context.LoadDataPath);
        Assert.Equal(Path.GetFullPath(savePath), context.SaveDataPath);
        Assert.Equal(Path.GetFullPath(mainFilePath), context.MainFilePath);
        var directoryContext = ProjectContext.CreateWithDirectories(Path.GetRelativePath(Environment.CurrentDirectory, directory), Path.GetRelativePath(Environment.CurrentDirectory, directory), Path.GetRelativePath(Environment.CurrentDirectory, mainFilePath));
        Assert.Equal(Path.GetFullPath(directory), directoryContext.LoadDirectory);
        Assert.Equal(Path.GetFullPath(mainFilePath), directoryContext.MainFilePath);
    }

    [Fact("ProjectContextTests.cs", 38)]
    public void ProjectPathAndFileOperationJsonConvertersRoundTripCompactForms()
    {
        var singlePath = JsonSerializer.Deserialize<ProjectMainOptions.PathList>("\"data.win\"");
        var num = 1;
        List<ProjectMainOptions.PathList.PathPair> list = new(num);
        CollectionsMarshal.SetCount(list, num);
        CollectionsMarshal.AsSpan(list)[0] = new ProjectMainOptions.PathList.PathPair("data.win", "data.win");
        Assert.Equal(list, singlePath.Paths);
        Assert.Equal("\"data.win\"", JsonSerializer.Serialize(singlePath));
        Assert.True(JsonSerializer.Deserialize<ProjectMainOptions.PathList>("null").Empty);
        var objectPath = JsonSerializer.Deserialize<ProjectMainOptions.PathList>("{\"Source\":\"input.win\",\"Destination\":\"output.win\"}");
        num = 1;
        List<ProjectMainOptions.PathList.PathPair> list2 = new(num);
        CollectionsMarshal.SetCount(list2, num);
        CollectionsMarshal.AsSpan(list2)[0] = new ProjectMainOptions.PathList.PathPair("input.win", "output.win");
        Assert.Equal(list2, objectPath.Paths);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProjectMainOptions.PathList>("42"));
        ProjectMainOptions.PathList pathList = new();
        num = 2;
        List<ProjectMainOptions.PathList.PathPair> list3 = new(num);
        CollectionsMarshal.SetCount(list3, num);
        var span = CollectionsMarshal.AsSpan(list3);
        span[0] = new ProjectMainOptions.PathList.PathPair("source.win", "destination.win");
        span[1] = new ProjectMainOptions.PathList.PathPair("shared.win", "shared.win");
        pathList.Paths = list3;
        var multiplePaths = pathList;
        var parsedPaths = JsonSerializer.Deserialize<ProjectMainOptions.PathList>(JsonSerializer.Serialize(multiplePaths));
        Assert.Equal(multiplePaths.Paths, parsedPaths.Paths);
        var requiredOperation = JsonSerializer.Deserialize<ProjectMainOptions.FileOperation>("\"data.win\"");
        Assert.True(requiredOperation.Required);
        Assert.Equal(singlePath.Paths, requiredOperation.Paths.Paths);
        ProjectMainOptions.FileOperation value = new()
        {
            Paths = multiplePaths,
            Required = false
        };
        var parsedOperation = JsonSerializer.Deserialize<ProjectMainOptions.FileOperation>(JsonSerializer.Serialize(value));
        Assert.False(parsedOperation.Required);
        Assert.Equal(multiplePaths.Paths, parsedOperation.Paths.Paths);
        var explicitRequiredOperation = JsonSerializer.Deserialize<ProjectMainOptions.FileOperation>("{\"Paths\":\"data.win\",\"Required\":true}");
        Assert.True(explicitRequiredOperation.Required);
        Assert.Equal(singlePath.Paths, explicitRequiredOperation.Paths.Paths);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProjectMainOptions.FileOperation>("{}"));
    }

    [Fact("ProjectContextTests.cs", 71)]
    public void ProjectRoundTripPreservesRoom()
    {
        var projectDirectory = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");
        var mainFilePath = Path.Combine(projectDirectory, "project.json");
        try
        {
            using var source = GameMakerData.CreateNew2026LTS();
            var sourceRoom = source.Rooms.ByName("room0");
            sourceRoom.Width = 640u;
            sourceRoom.Height = 480u;
            sourceRoom.Speed = 60u;
            ProjectContext projectContext = new(source, "input.win", "output.win", mainFilePath, "Test");
            Assert.True(projectContext.MarkAssetForExport(sourceRoom));
            projectContext.Export(clearMarkedAssets: true);
            using (var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(projectDirectory, "rooms", "room0.json"))))
            {
                Assert.Equal(640u, document.RootElement.GetProperty("Width").GetUInt32());
            }
            using var destination = GameMakerData.CreateNew2026LTS();
            ProjectContext.CreateWithDirectories(projectDirectory, projectDirectory, mainFilePath).Import(destination, new GameFileNoOpBackup());
            Assert.Single(destination.Rooms);
            var importedRoom = destination.Rooms.ByName("room0");
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
        var projectDirectory = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");
        var mainFilePath = Path.Combine(projectDirectory, "project.json");
        try
        {
            using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "data-2026.win"));
            using var source = GameMakerIO.Read(input);
            var sourceSprite = source.Sprites.First(sprite => sprite != null);
            var sourceName = sourceSprite.Name.Content;
            var sourceWidth = sourceSprite.Width;
            var sourceHeight = sourceSprite.Height;
            ProjectContext projectContext = new(source, "input.win", "output.win", mainFilePath, "Test");
            Assert.True(projectContext.MarkAssetForExport(sourceSprite));
            projectContext.Export(clearMarkedAssets: true);
            using var destination = GameMakerData.CreateNew2026LTS();
            ProjectContext.CreateWithDirectories(projectDirectory, projectDirectory, mainFilePath).Import(destination, new GameFileNoOpBackup());
            var importedSprite = destination.Sprites.ByName(sourceName);
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
        var projectDirectory = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");
        var mainFilePath = Path.Combine(projectDirectory, "project.json");
        List<IProjectAsset> list = [];
        try
        {
            using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
            using var source = GameMakerIO.Read(input);
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
            var destination = GameMakerData.CreateNew2026LTS();
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
                    var sourceSystem = source.ParticleSystems.First(system => system != null);
                    var importedSystem = destination.ParticleSystems.ByName(sourceSystem.Name.Content);
                    Assert.Equal(sourceSystem.OriginX, importedSystem.OriginX);
                    Assert.Equal(sourceSystem.OriginY, importedSystem.OriginY);
                    Assert.Equal(sourceSystem.DrawOrder, importedSystem.DrawOrder);
                    Assert.Equal(((Collection<GameMakerResourceById<GameMakerParticleSystemEmitter, GameMakerChunkPSEM>>)(object)sourceSystem.Emitters).Count, ((Collection<GameMakerResourceById<GameMakerParticleSystemEmitter, GameMakerChunkPSEM>>)(object)importedSystem.Emitters).Count);
                    var sourceEmitter = sourceSystem.Emitters.FirstOrDefault(reference => reference?.Resource != null)?.Resource;
                    if (sourceEmitter != null)
                    {
                        var importedEmitter = destination.ParticleSystemEmitters.ByName(sourceEmitter.Name.Content);
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

    [Fact]
    public void ScriptsAreNotAllowedUntilTheHostTurnsThemOn()
    {
        var context = ProjectContext.CreateWithDataFilePaths("input.win", "output.win", "project.json");

        Assert.False(context.AllowScripts);
    }
}
