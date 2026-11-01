using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using G3MLib.DataFile;
using G3MLib.DataFile.Project;
using G3MLib.Modding.Resources;

namespace G3MLib.Tests;

public sealed class G3MProjectTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    public static TheoryData<string> Fixtures() => ["data-2022.win", "data-2023.win", "data-2024.win", "data-2026.win"];

    private string FixtureCopy(string fixture)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, fixture);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), path);
        return path;
    }

    private static G3MProjectResult Export(string baseFile, string directory)
    {
        using var input = File.OpenRead(baseFile);
        using var data = GameMakerIO.Read(input);
        return G3MProjectService.Export(data, directory, baseFile, new G3MProjectOptions { Name = "Test" });
    }

    private static Dictionary<string, byte[]> Snapshot(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(directory, f).Replace('\\', '/'), File.ReadAllBytes);

    private static void EditObjectDepth(string directory)
    {
        var file = Path.Combine(directory, "objects", "obj_probe__idx0000", "object.json");
        var text = File.ReadAllText(file);
        Assert.Contains("\"depth\": 0,", text, StringComparison.Ordinal);
        File.WriteAllText(file, text.Replace("\"depth\": 0,", "\"depth\": 25,", StringComparison.Ordinal));
    }

    [Fact]
    public void ExportWritesProjectFileAndGameMakerStyleFolders()
    {
        var baseFile = FixtureCopy("data-2026.win");
        var directory = Path.Combine(_root, "project");
        var result = Export(baseFile, directory);
        Assert.Empty(result.Warnings);
        Assert.Equal(Path.Combine(directory, "Test.g3mproj"), result.Path);
        using var project = JsonDocument.Parse(File.ReadAllText(result.Path));
        var root = project.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("full", root.GetProperty("mode").GetString());
        Assert.Equal(new FileInfo(baseFile).Length, root.GetProperty("baseFile").GetProperty("size").GetInt64());
        Assert.Contains(root.GetProperty("resources").EnumerateArray(), r => r.GetProperty("id").GetProperty("path").GetString() == "sprites/spr_probe");
        Assert.True(File.Exists(Path.Combine(directory, "sprites", "spr_probe", "spr_probe.json")));
        Assert.True(File.Exists(Path.Combine(directory, "rooms", "rm_probe", "room.json")));
        Assert.True(root.GetProperty("resourceOrder").TryGetProperty("Rooms", out _));
        Assert.Empty(Directory.EnumerateDirectories(directory, ".staging-*"));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ExportTwiceProducesIdenticalBytes(string fixture)
    {
        var baseFile = FixtureCopy(fixture);
        Export(baseFile, Path.Combine(_root, "a"));
        Export(baseFile, Path.Combine(_root, "b"));
        var a = Snapshot(Path.Combine(_root, "a"));
        var b = Snapshot(Path.Combine(_root, "b"));
        Assert.Equal(a.Keys.Order(StringComparer.Ordinal), b.Keys.Order(StringComparer.Ordinal));
        Assert.All(a, pair => Assert.Equal(pair.Value, b[pair.Key]));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ExportingOverAnExistingProjectChangesNoFile(string fixture)
    {
        var baseFile = FixtureCopy(fixture);
        var directory = Path.Combine(_root, "project");
        Export(baseFile, directory);
        var first = Snapshot(directory);
        Export(baseFile, directory);
        var second = Snapshot(directory);
        Assert.Equal(first.Keys.Order(StringComparer.Ordinal), second.Keys.Order(StringComparer.Ordinal));
        Assert.All(first, pair => Assert.Equal(pair.Value, second[pair.Key]));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TextFilesAreUtf8WithoutBomLfAndFinalNewline(string fixture)
    {
        var directory = Path.Combine(_root, "project");
        Export(FixtureCopy(fixture), directory);
        string[] extensions = [".json", ".gml", ".asm", ".g3mproj"];
        string[] files = [.. Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f)))];
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble), $"BOM in {file}");
            Assert.DoesNotContain((byte)'\r', bytes);
            Assert.True(bytes.Length > 0 && bytes[^1] == (byte)'\n', $"missing final newline in {file}");
            Assert.False(bytes.Length > 1 && bytes[^2] == (byte)'\n', $"extra blank line at end of {file}");
        }
    }

    [Fact]
    public void ExportTextNormalizesJsonGmlAndAsmOnlyForProjectExports()
    {
        Directory.CreateDirectory(_root);
        var json = Path.Combine(_root, "a.json");
        var text = Path.Combine(_root, "a.txt");
        try
        {
            ExportText.NormalizeForProject = true;
            ExportText.WriteAllText(json, "{\r\n}\r\n\r\n", Encoding.UTF8);
            ExportText.WriteAllText(text, "x\r\n", Encoding.UTF8);
        }
        finally
        {
            ExportText.NormalizeForProject = false;
        }
        Assert.Equal("{\n}\n"u8.ToArray(), File.ReadAllBytes(json));
        Assert.True(File.ReadAllBytes(text).AsSpan().StartsWith(Encoding.UTF8.Preamble));

        ExportText.WriteAllText(json, "{\r\n}", Encoding.UTF8);
        Assert.True(File.ReadAllBytes(json).AsSpan().StartsWith(Encoding.UTF8.Preamble));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void FullExportThenBuildIsByteIdentical(string fixture)
    {
        var baseFile = FixtureCopy(fixture);
        var exported = Export(baseFile, Path.Combine(_root, "project"));
        var rebuilt = Path.Combine(_root, "rebuilt.win");
        var built = G3MProjectService.Build(exported.Path, baseFile, rebuilt);
        Assert.Empty(built.Warnings);
        Assert.Equal(0, built.Resources);
        Assert.Equal(exported.Resources, built.Unchanged);
        Assert.Equal(File.ReadAllBytes(baseFile), File.ReadAllBytes(rebuilt));
    }

    [Fact]
    public void BuildImportsOnlyChangedResources()
    {
        var baseFile = FixtureCopy("data-2026.win");
        var exported = Export(baseFile, Path.Combine(_root, "project"));
        EditObjectDepth(Path.Combine(_root, "project"));

        var rebuilt = Path.Combine(_root, "rebuilt.win");
        var built = G3MProjectService.Build(exported.Path, baseFile, rebuilt);
        Assert.Equal(1, built.Resources);
        Assert.Equal(exported.Resources - 1, built.Unchanged);
        using var stream = File.OpenRead(rebuilt);
        using var result = GameMakerIO.Read(stream);
        Assert.Equal(25, result.GameObjects.ByName("obj_probe").Depth);
    }

    [Fact]
    public void BuildWarnsAndImportsEverythingWhenBaseFileDiffers()
    {
        var baseFile = FixtureCopy("data-2022.win");
        var exported = Export(baseFile, Path.Combine(_root, "project"));
        var recorded = JsonDocument.Parse(File.ReadAllText(exported.Path)).RootElement.GetProperty("baseFile").GetProperty("md5").GetString();
        File.WriteAllText(exported.Path, File.ReadAllText(exported.Path).Replace(recorded, new string('0', 32), StringComparison.Ordinal));
        var built = G3MProjectService.Build(exported.Path, baseFile, Path.Combine(_root, "rebuilt.win"));
        Assert.Contains(built.Warnings, w => w.Contains("base data file", StringComparison.Ordinal));
        Assert.Equal(0, built.Unchanged);
        Assert.Equal(exported.Resources, built.Resources);
    }

    [Theory]
    [InlineData("\"formatVersion\": 2", "newer")]
    [InlineData("\"formatVersion\": 0", "unknown")]
    public void BuildRefusesAnUnsupportedFormatVersionWithAClearMessage(string replacement, string expected)
    {
        var baseFile = FixtureCopy("data-2022.win");
        var exported = Export(baseFile, Path.Combine(_root, "project"));
        File.WriteAllText(exported.Path, File.ReadAllText(exported.Path).Replace("\"formatVersion\": 1", replacement, StringComparison.Ordinal));
        var error = Assert.Throws<ProjectException>(() => G3MProjectService.Build(exported.Path, baseFile, Path.Combine(_root, "rebuilt.win")));
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.Contains("version", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportRefusesToOverwriteAProjectFromANewerVersion()
    {
        var baseFile = FixtureCopy("data-2022.win");
        var exported = Export(baseFile, Path.Combine(_root, "project"));
        File.WriteAllText(exported.Path, File.ReadAllText(exported.Path).Replace("\"formatVersion\": 1", "\"formatVersion\": 2", StringComparison.Ordinal));
        Assert.Throws<ProjectException>(() => Export(baseFile, Path.Combine(_root, "project")));
    }

    [Fact]
    public void BuildAppliesRecordedResourceOrder()
    {
        var baseFile = FixtureCopy("data-2026.win");
        var exported = Export(baseFile, Path.Combine(_root, "project"));
        var project = JsonNode.Parse(File.ReadAllText(exported.Path));
        var rooms = project["resourceOrder"]["Rooms"].AsArray();
        string[] original = [.. rooms.Select(n => n.GetValue<string>())];
        Assert.True(original.Length > 1);
        string[] reversed = [.. original.Reverse()];
        project["resourceOrder"]["Rooms"] = new JsonArray([.. reversed.Select(n => (JsonNode)JsonValue.Create(n))]);
        File.WriteAllText(exported.Path, project.ToJsonString());

        var rebuilt = Path.Combine(_root, "rebuilt.win");
        G3MProjectService.Build(exported.Path, baseFile, rebuilt);
        using var stream = File.OpenRead(rebuilt);
        using var after = GameMakerIO.Read(stream);
        Assert.Equal(reversed, after.Rooms.Select(r => r.Name.Content));
    }

    [Fact]
    public void DeltaExportWritesOnlyTheNamedResources()
    {
        var baseFile = FixtureCopy("data-2026.win");
        using var input = File.OpenRead(baseFile);
        using var data = GameMakerIO.Read(input);
        var directory = Path.Combine(_root, "project");
        var result = G3MProjectService.Export(data, directory, baseFile, new G3MProjectOptions
        {
            Name = "Delta",
            Mode = ProjectExportMode.Delta,
            Only = new Dictionary<string, IReadOnlySet<string>> { ["Rooms"] = new HashSet<string> { "rm_probe" } }
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, result.Resources);
        Assert.True(File.Exists(Path.Combine(directory, "rooms", "rm_probe", "room.json")));
        Assert.False(Directory.Exists(Path.Combine(directory, "sprites")));
        using var project = JsonDocument.Parse(File.ReadAllText(result.Path));
        Assert.Equal("delta", project.RootElement.GetProperty("mode").GetString());
    }

    // Progress<T> posts to the thread pool, so reports could still be in flight when the assertions ran.
    private sealed class SyncProgress(Action<GameMakerProgress> onReport) : IProgress<GameMakerProgress>
    {
        public void Report(GameMakerProgress value) => onReport(value);
    }

    [Fact]
    public async Task OpenReturnsTheResourceIndexWithoutReadingResourceFolders()
    {
        var baseFile = FixtureCopy("data-2026.win");
        var exported = Export(baseFile, Path.Combine(_root, "project"));
        List<GameMakerProgress> reports = [];
        var project = await G3MProjectService.OpenAsync(exported.Path, new SyncProgress(reports.Add), TestContext.Current.CancellationToken);
        Assert.Equal("Test", project.Name);
        Assert.Equal(G3MProjectService.FormatVersion, project.FormatVersion);
        Assert.Equal("full", project.Mode);
        Assert.Equal(Path.GetDirectoryName(exported.Path), project.Directory);
        Assert.Equal(new FileInfo(baseFile).Length, project.BaseFile.Size);
        Assert.Equal(exported.Resources, project.Resources.Count);
        var sprite = project.Find("sprites/spr_probe");
        Assert.NotNull(sprite);
        Assert.Equal("Sprites", sprite.Type);
        Assert.Equal("spr_probe", sprite.Name);
        Assert.Equal(Path.Combine(project.Directory, "sprites", "spr_probe"), project.GetPath(sprite));
        Assert.All(project.OfType("Rooms"), r => Assert.StartsWith("rooms/", r.Path, StringComparison.Ordinal));
        Assert.Contains("rm_probe", project.ResourceOrder["Rooms"]);
        Assert.Null(project.Find("sprites/missing"));
        Assert.Equal(1.0, reports.Last().Fraction);
    }

    [Fact]
    public async Task OpenHonoursCancellationAndRejectsNewerFormats()
    {
        var baseFile = FixtureCopy("data-2026.win");
        var exported = Export(baseFile, Path.Combine(_root, "project"));
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => G3MProjectService.OpenAsync(exported.Path, null, cancelled.Token));

        var node = JsonNode.Parse(File.ReadAllText(exported.Path));
        node["formatVersion"] = G3MProjectService.FormatVersion + 1;
        File.WriteAllText(exported.Path, node.ToJsonString());
        await Assert.ThrowsAsync<ProjectException>(() => G3MProjectService.OpenAsync(exported.Path, null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ProjectException>(() => G3MProjectService.OpenAsync(Path.Combine(_root, "none.g3mproj"), null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void UtmtLayoutExportWarnsAboutWhatTheLayoutCannotHold()
    {
        var baseFile = FixtureCopy("data-2026.win");
        using var input = File.OpenRead(baseFile);
        using var data = GameMakerIO.Read(input);
        ProjectContext context = new(data, baseFile, Path.Combine(_root, "out.win"), Path.Combine(_root, "utmt", "project.json"), "Utmt");
        context.ExportAll();
        var warning = Assert.Single(context.Warnings);
        Assert.Contains("event action", warning, StringComparison.Ordinal);
        Assert.Contains("layer effect", warning, StringComparison.Ordinal);
        Assert.Contains("texture", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void DeltaBuildAppliesRecordedDeletions()
    {
        var baseFile = FixtureCopy("data-2026.win");
        using var input = File.OpenRead(baseFile);
        using var data = GameMakerIO.Read(input);
        Assert.NotNull(data.Rooms.ByName("Room2"));
        var exported = G3MProjectService.Export(data, Path.Combine(_root, "project"), baseFile, new G3MProjectOptions
        {
            Name = "Delta",
            Mode = ProjectExportMode.Delta,
            Only = new Dictionary<string, IReadOnlySet<string>> { ["Rooms"] = new HashSet<string> { "rm_probe" } },
            Deleted = new Dictionary<string, IReadOnlySet<string>> { ["Rooms"] = new HashSet<string> { "Room2" } }
        }, cancellationToken: TestContext.Current.CancellationToken);
        using (var project = JsonDocument.Parse(File.ReadAllText(exported.Path)))
        {
            var deleted = Assert.Single(project.RootElement.GetProperty("deleted").EnumerateArray());
            Assert.Equal("Rooms", deleted.GetProperty("type").GetString());
            Assert.Equal("Room2", deleted.GetProperty("name").GetString());
        }

        var rebuilt = Path.Combine(_root, "rebuilt.win");
        var built = G3MProjectService.Build(exported.Path, baseFile, rebuilt);
        Assert.Empty(built.Warnings);
        using var stream = File.OpenRead(rebuilt);
        using var after = GameMakerIO.Read(stream);
        Assert.Null(after.Rooms.ByName("Room2"));
        Assert.NotNull(after.Rooms.ByName("rm_probe"));
    }

    [Fact]
    public void ExportingAgainKeepsRecordedDeletions()
    {
        var baseFile = FixtureCopy("data-2026.win");
        using var input = File.OpenRead(baseFile);
        using var data = GameMakerIO.Read(input);
        var directory = Path.Combine(_root, "project");
        G3MProjectOptions first = new() { Name = "Keep", Deleted = new Dictionary<string, IReadOnlySet<string>> { ["Rooms"] = new HashSet<string> { "Room2" } } };
        G3MProjectService.Export(data, directory, baseFile, first, cancellationToken: TestContext.Current.CancellationToken);
        var second = G3MProjectService.Export(data, directory, baseFile, new G3MProjectOptions { Name = "Keep" }, cancellationToken: TestContext.Current.CancellationToken);
        using var project = JsonDocument.Parse(File.ReadAllText(second.Path));
        Assert.Single(project.RootElement.GetProperty("deleted").EnumerateArray());
    }

    private sealed class SyncBatchProgress(Action<BatchProgress> onReport) : IProgress<BatchProgress>
    {
        public void Report(BatchProgress value) => onReport(value);
    }

    private static Dictionary<string, (string Type, string Name, string Hash)> Index(string directory)
    {
        using var project = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "Test.g3mproj")));
        return project.RootElement.GetProperty("resources").EnumerateArray().ToDictionary(
            r => r.GetProperty("id").GetProperty("path").GetString(),
            r => (r.GetProperty("type").GetString(), r.GetProperty("id").GetProperty("name").GetString(), r.GetProperty("hash").GetString()));
    }

    // The resources whose files differ between two exports, by type and resource name, including ones only the first has.
    private static Dictionary<string, IReadOnlySet<string>> ChangedResources(string before, string after)
    {
        Dictionary<string, (string Type, string Name, string Hash)> a = Index(before), b = Index(after);
        Dictionary<string, IReadOnlySet<string>> changed = [];
        foreach (var path in a.Keys.Union(b.Keys).Where(p => !a.TryGetValue(p, out var x) || !b.TryGetValue(p, out var y) || x.Hash != y.Hash))
        {
            var (type, unit, _) = a.TryGetValue(path, out var found) ? found : b[path];
            var name = Regex.Replace(unit, @"__idx\d{4}$", "");
            ((HashSet<string>)(changed.TryGetValue(type, out var set) ? set : changed[type] = new HashSet<string>())).Add(name);
        }
        return changed;
    }

    private static void AssertSameFiles(string expected, string actual)
    {
        Dictionary<string, byte[]> want = Snapshot(expected), got = Snapshot(actual);
        Assert.Equal(want.Keys.Order(StringComparer.Ordinal), got.Keys.Order(StringComparer.Ordinal));
        Assert.All(want, file => Assert.True(file.Value.AsSpan().SequenceEqual(got[file.Key]), $"{file.Key} differs"));
    }

    [Fact]
    public void ExportReportsProgressPerResourceAndEndsComplete()
    {
        var baseFile = FixtureCopy("data-2026.win");
        using var input = File.OpenRead(baseFile);
        using var data = GameMakerIO.Read(input);
        List<BatchProgress> reports = [];
        G3MProjectService.Export(data, Path.Combine(_root, "project"), baseFile, new G3MProjectOptions { Name = "Test" }, new SyncBatchProgress(r => { lock (reports) { reports.Add(r); } }), TestContext.Current.CancellationToken);
        Assert.NotEmpty(reports);
        Assert.Equal(reports.Max(r => r.Total), reports.Max(r => r.Done));
        Assert.True(reports.Max(r => r.Total) >= data.Sprites.Count + data.GameObjects.Count + data.Rooms.Count);
        Assert.Contains(reports, r => r.Item == "rm_probe");
        Assert.Contains(reports, r => r.Item == "spr_probe");
    }

    [Fact]
    public void CancelledExportLeavesAnExistingProjectUnchanged()
    {
        var baseFile = FixtureCopy("data-2026.win");
        var directory = Path.Combine(_root, "project");
        Export(baseFile, directory);
        var before = Snapshot(directory);
        using var input = File.OpenRead(baseFile);
        using var data = GameMakerIO.Read(input);
        EditObjectDepthInModel(data);
        using CancellationTokenSource cancel = new();
        Assert.ThrowsAny<OperationCanceledException>(() => G3MProjectService.Export(
            data, directory, baseFile, new G3MProjectOptions { Name = "Test" }, new SyncBatchProgress(_ => cancel.Cancel()), cancel.Token));
        var after = Snapshot(directory);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        Assert.All(before, file => Assert.True(file.Value.AsSpan().SequenceEqual(after[file.Key]), $"{file.Key} changed"));
    }

    [Fact]
    public void CancelledExportOfANewProjectLeavesNoFolder()
    {
        var baseFile = FixtureCopy("data-2026.win");
        var directory = Path.Combine(_root, "project");
        using var input = File.OpenRead(baseFile);
        using var data = GameMakerIO.Read(input);
        using CancellationTokenSource cancel = new();
        Assert.ThrowsAny<OperationCanceledException>(() => G3MProjectService.Export(
            data, directory, baseFile, new G3MProjectOptions { Name = "Test" }, new SyncBatchProgress(_ => cancel.Cancel()), cancel.Token));
        Assert.False(Directory.Exists(directory));
    }

    private static void EditObjectDepthInModel(GameMakerData data) => data.GameObjects.ByName("obj_probe").Depth = 25;

    [Fact]
    public void UpdateOfOneResourceEqualsAFullExport()
    {
        var baseFile = FixtureCopy("data-2026.win");
        var directory = Path.Combine(_root, "project");
        Export(baseFile, directory);
        using var input = File.OpenRead(baseFile);
        using var data = GameMakerIO.Read(input);
        EditObjectDepthInModel(data);
        G3MProjectService.Export(data, Path.Combine(_root, "full"), baseFile, new G3MProjectOptions { Name = "Test" }, cancellationToken: TestContext.Current.CancellationToken);
        List<BatchProgress> reports = [];
        var result = G3MProjectService.Update(data, directory, baseFile, new G3MProjectOptions
        {
            Name = "Test",
            Only = new Dictionary<string, IReadOnlySet<string>> { ["GameObjects"] = new HashSet<string> { "obj_probe" } }
        }, new SyncBatchProgress(reports.Add), TestContext.Current.CancellationToken);
        Assert.Equal(1, result.Resources);
        Assert.Equal(["obj_probe"], reports.Select(r => r.Item).Where(i => i.Length > 0).Distinct());
        AssertSameFiles(Path.Combine(_root, "full"), directory);
    }

    [Fact]
    public void UpdateAfterARenameRemovesTheOldNameAndEqualsAFullExport()
    {
        var baseFile = FixtureCopy("data-2026.win");
        var directory = Path.Combine(_root, "project");
        Export(baseFile, directory);
        using var input = File.OpenRead(baseFile);
        using var data = GameMakerIO.Read(input);
        DataFile.References.RenameService.RenameResource(data, data.GameObjects.ByName("obj_probe"), "obj_probe_renamed");
        var full = Path.Combine(_root, "full");
        G3MProjectService.Export(data, full, baseFile, new G3MProjectOptions { Name = "Test" }, cancellationToken: TestContext.Current.CancellationToken);
        var changed = ChangedResources(directory, full);
        Assert.Contains("obj_probe", changed["GameObjects"]);
        Assert.Contains("obj_probe_renamed", changed["GameObjects"]);
        Assert.True(changed.Count > 1, "the rename should also change resources that refer to the object");
        G3MProjectService.Update(data, directory, baseFile, new G3MProjectOptions { Name = "Test", Only = changed }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(Directory.Exists(Path.Combine(directory, "objects", "obj_probe")));
        AssertSameFiles(full, directory);
    }

    [Fact]
    public void UpdateKeepsTheRecordedHashOfAnEditedFileAndRefusesAMissingProject()
    {
        var baseFile = FixtureCopy("data-2026.win");
        var directory = Path.Combine(_root, "project");
        Export(baseFile, directory);
        EditObjectDepth(directory);
        using var input = File.OpenRead(baseFile);
        using var data = GameMakerIO.Read(input);
        G3MProjectService.Update(data, directory, baseFile, new G3MProjectOptions
        {
            Name = "Test",
            Only = new Dictionary<string, IReadOnlySet<string>> { ["Rooms"] = new HashSet<string> { "rm_probe" } }
        }, cancellationToken: TestContext.Current.CancellationToken);
        var built = G3MProjectService.Build(Path.Combine(directory, "Test.g3mproj"), baseFile, Path.Combine(_root, "rebuilt.win"));
        Assert.Equal(1, built.Resources);
        Assert.Throws<ProjectException>(() => G3MProjectService.Update(data, Path.Combine(_root, "none"), baseFile, new G3MProjectOptions
        {
            Name = "Test",
            Only = new Dictionary<string, IReadOnlySet<string>>()
        }, cancellationToken: TestContext.Current.CancellationToken));
    }
}
