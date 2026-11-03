using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.Modding.Resources;

namespace G3MLib.Tests;

public sealed class BatchExportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");

    public BatchExportTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class SyncProgress(Action<BatchProgress> onReport) : IProgress<BatchProgress>
    {
        public void Report(BatchProgress value) => onReport(value);
    }

    private static GameMakerData CreateData()
    {
        var data = GameMakerData.CreateNew2026LTS();
        foreach (var name in new[] { "first", "second" })
        {
            data.GameObjects.Add(new GameMakerGameObject { Name = data.Strings.MakeString(name) });
            data.Scripts.Add(new GameMakerScript { Name = data.Strings.MakeString("scr_" + name) });
        }
        return data;
    }

    private static Dictionary<string, byte[]> ReadTree(string directory) =>
        Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(directory, path), File.ReadAllBytes);

    [Fact("BatchExportTests.cs", 1)]
    public void FailedItemIsReportedAndLaterItemsRun()
    {
        using var data = CreateData();
        var output = Path.Combine(_root, "out");
        Directory.CreateDirectory(Path.Combine(output, "GameObjects"));
        File.WriteAllText(Path.Combine(output, "GameObjects", "first__idx0000"), "blocks the folder");

        var results = ResourceExportService.ExportGameObjects(data, output, null, null, CancellationToken.None);

        var failed = Assert.Single(results, result => result.Kind == ResultKind.Failed);
        Assert.Equal("first", failed.Name);
        Assert.False(string.IsNullOrEmpty(failed.Message));
        Assert.Contains(results, result => result.Name == "second" && result.Kind == ResultKind.Written);
        Assert.True(File.Exists(Path.Combine(output, "GameObjects", "second__idx0001", "object.json")));
    }

    [Fact("BatchExportTests.cs", 2)]
    public void CancellationThrowsAndRemovesWhatTheRunAdded()
    {
        using var data = CreateData();
        var output = Path.Combine(_root, "out");
        using CancellationTokenSource source = new();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            ResourceExportService.ExportGameObjects(data, output, null, new SyncProgress(_ => source.Cancel()), source.Token));

        Assert.False(Directory.Exists(output));
    }

    [Fact("BatchExportTests.cs", 3)]
    public void ProgressEndsWithDoneEqualToTotal()
    {
        using var data = CreateData();
        List<BatchProgress> reports = [];
        SyncProgress progress = new(report => { lock (reports) reports.Add(report); });

        ResourceExportService.ExportGameObjects(data, Path.Combine(_root, "out"), new HashSet<string> { "second" }, progress, CancellationToken.None);

        var last = reports.OrderBy(report => report.Done).Last();
        Assert.Equal(last.Total, last.Done);
        Assert.Equal(2, last.Total);
    }

    [Fact("BatchExportTests.cs", 4)]
    public void OldAndNewOverloadsWriteIdenticalFiles()
    {
        using var data = CreateData();
        var oldOutput = Path.Combine(_root, "old");
        var newOutput = Path.Combine(_root, "new");

        ResourceExportService.ExportGameObjects(data, oldOutput);
        ResourceExportService.ExportScripts(data, oldOutput);
        ResourceExportService.ExportGameObjects(data, newOutput, null, null, CancellationToken.None);
        ResourceExportService.ExportScripts(data, newOutput, null, null, CancellationToken.None);

        var expected = ReadTree(oldOutput);
        var actual = ReadTree(newOutput);
        Assert.NotEmpty(expected);
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        Assert.All(expected, pair => Assert.Equal(pair.Value, actual[pair.Key]));
    }

    [Fact("BatchExportTests.cs", 5)]
    public async Task ConcurrentExportsToDifferentFoldersBothSucceed()
    {
        using var data = CreateData();
        var first = Path.Combine(_root, "a");
        var second = Path.Combine(_root, "b");

        var results = await Task.WhenAll(
            Task.Run(() => ResourceExportService.ExportGameObjects(data, first, null, null, CancellationToken.None), TestContext.Current.CancellationToken),
            Task.Run(() => ResourceExportService.ExportGameObjects(data, second, null, null, CancellationToken.None), TestContext.Current.CancellationToken));

        Assert.All(results, run => Assert.Equal(2, run.Count(result => result.Kind == ResultKind.Written)));
        Assert.Equal(ReadTree(first).Keys.Order(), ReadTree(second).Keys.Order());
    }

    [Fact("BatchExportTests.cs", 6)]
    public void ImportReportsEachItem()
    {
        using var source = CreateData();
        var folder = Path.Combine(_root, "export");
        ResourceExportService.ExportGameObjects(source, folder);
        using var target = GameMakerData.CreateNew2026LTS();
        List<BatchProgress> reports = [];

        var results = ResourceImportService.Import("GameObjects", target, Path.Combine(folder, "GameObjects"), new SyncProgress(reports.Add), CancellationToken.None);

        Assert.Equal(["first", "second"], results.Where(result => result.Kind == ResultKind.Written).Select(result => result.Name).Order());
        Assert.Equal(2, reports.Max(report => report.Done));
    }

    [Fact("BatchExportTests.cs", 7)]
    public void ImportOnlyImportsNamedEntriesAndMatchesDiscardingTheRest()
    {
        using var source = CreateData();
        source.GameObjects[0].Depth = 10;
        source.GameObjects[1].Depth = 42;
        var folder = Path.Combine(_root, "export_only");
        ResourceExportService.ExportGameObjects(source, folder);
        ResourceExportService.ExportScripts(source, folder);

        using var selective = GameMakerData.CreateNew2026LTS();
        List<BatchProgress> reports = [];
        var objectResults = ResourceImportService.Import(
            "GameObjects",
            selective,
            Path.Combine(folder, "GameObjects"),
            new HashSet<string> { "second" },
            new SyncProgress(reports.Add),
            CancellationToken.None);

        var writtenObject = Assert.Single(objectResults);
        Assert.Equal("second", writtenObject.Name);
        Assert.Equal(ResultKind.Written, writtenObject.Kind);
        var lastReport = reports.OrderBy(report => report.Done).Last();
        Assert.Equal(1, lastReport.Total);
        Assert.Equal(1, lastReport.Done);
        Assert.Equal("second", lastReport.Item);

        var scriptResults = ResourceImportService.Import(
            "Scripts",
            selective,
            Path.Combine(folder, "Scripts"),
            new HashSet<string> { "scr_second" },
            progress: null,
            CancellationToken.None);
        var writtenScript = Assert.Single(scriptResults);
        Assert.Equal("scr_second", writtenScript.Name);
        Assert.Equal(ResultKind.Written, writtenScript.Kind);

        using var fullThenDiscarded = GameMakerData.CreateNew2026LTS();
        ResourceImportService.Import("GameObjects", fullThenDiscarded, Path.Combine(folder, "GameObjects"), CancellationToken.None);
        ResourceImportService.Import("Scripts", fullThenDiscarded, Path.Combine(folder, "Scripts"), CancellationToken.None);
        fullThenDiscarded.GameObjects.Remove(fullThenDiscarded.GameObjects.ByName("first"));
        fullThenDiscarded.Scripts.Remove(fullThenDiscarded.Scripts.ByName("scr_first"));

        Assert.Equal(
            fullThenDiscarded.GameObjects.Select(o => (o.Name?.Content, o.Depth)),
            selective.GameObjects.Select(o => (o.Name?.Content, o.Depth)));
        Assert.Equal(
            fullThenDiscarded.Scripts.Select(s => s.Name?.Content),
            selective.Scripts.Select(s => s.Name?.Content));
    }

    [Fact("BatchExportTests.cs", 8)]
    public void ImportOnlyDictionarySupportsProgressAndCancellation()
    {
        using var source = CreateData();
        var folder = Path.Combine(_root, "export_dict");
        ResourceExportService.ExportGameObjects(source, folder);
        ResourceExportService.ExportScripts(source, folder);

        var only = new Dictionary<string, IReadOnlySet<string>>
        {
            ["GameObjects"] = new HashSet<string> { "second" },
            ["Scripts"] = new HashSet<string> { "scr_first" }
        };

        using var target = GameMakerData.CreateNew2026LTS();
        List<BatchProgress> reports = [];
        var results = ResourceImportService.Import(target, folder, only, new SyncProgress(reports.Add), CancellationToken.None);

        Assert.Equal(["scr_first", "second"], results.Where(r => r.Kind == ResultKind.Written).Select(r => r.Name));
        Assert.Equal(["second"], target.GameObjects.Select(o => o.Name?.Content));
        Assert.Equal(["scr_first"], target.Scripts.Select(s => s.Name?.Content));
        Assert.NotEmpty(reports);

        using var cancelledTarget = GameMakerData.CreateNew2026LTS();
        using CancellationTokenSource cts = new();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            ResourceImportService.Import(
                "GameObjects",
                cancelledTarget,
                Path.Combine(folder, "GameObjects"),
                new HashSet<string> { "first", "second" },
                new SyncProgress(_ => cts.Cancel()),
                cts.Token));
    }

    [Theory]
    [InlineData("Language")]
    [InlineData("Options")]
    [InlineData("GlobalScripts")]
    [InlineData("FeatureFlags")]
    [InlineData("Tags")]
    [InlineData("GeneralInfo")]
    [InlineData("TexturePageItems")]
    public void ImportWithOnlyThrowsNotSupportedExceptionForUnsupportedTypes(string resourceType)
    {
        using var target = GameMakerData.CreateNew2026LTS();
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResourceImportService.Import(resourceType, target, _root, new HashSet<string> { "test" }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains(resourceType, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportScriptsHonoursOnlyWithLooseJsonFiles()
    {
        var scriptsDir = Path.Combine(_root, "loose_scripts");
        Directory.CreateDirectory(scriptsDir);
        File.WriteAllText(Path.Combine(scriptsDir, "scr_a.json"), """{"name":"scr_a","code":"gml_Script_scr_a"}""");
        File.WriteAllText(Path.Combine(scriptsDir, "scr_b.json"), """{"name":"scr_b","code":"gml_Script_scr_b"}""");

        using var target = GameMakerData.CreateNew2026LTS();
        var results = ResourceImportService.Import("Scripts", target, scriptsDir, new HashSet<string> { "scr_b" }, cancellationToken: TestContext.Current.CancellationToken);
        var written = Assert.Single(results);
        Assert.Equal("scr_b", written.Name);
        Assert.NotNull(target.Scripts.ByName("scr_b"));
        Assert.Null(target.Scripts.ByName("scr_a"));
    }

    [Fact]
    public void SingleTypeAndDictionaryOverloadsBothResolveTypeSubfolder()
    {
        using var source = CreateData();
        var rootExport = Path.Combine(_root, "resolve_subfolder");
        ResourceExportService.ExportScripts(source, rootExport);

        Assert.True(Directory.Exists(Path.Combine(rootExport, "Scripts")));

        using var targetSingle = GameMakerData.CreateNew2026LTS();
        var singleResults = ResourceImportService.Import("Scripts", targetSingle, rootExport, new HashSet<string> { "scr_first" }, cancellationToken: TestContext.Current.CancellationToken);
        var singleWritten = Assert.Single(singleResults);
        Assert.Equal("scr_first", singleWritten.Name);
        Assert.NotNull(targetSingle.Scripts.ByName("scr_first"));

        using var targetDict = GameMakerData.CreateNew2026LTS();
        var dictResults = ResourceImportService.Import(targetDict, rootExport, new Dictionary<string, IReadOnlySet<string>>
        {
            ["Scripts"] = new HashSet<string> { "scr_second" }
        }, cancellationToken: TestContext.Current.CancellationToken);
        var dictWritten = Assert.Single(dictResults);
        Assert.Equal("scr_second", dictWritten.Name);
        Assert.NotNull(targetDict.Scripts.ByName("scr_second"));
    }

    [Fact]
    public void ImportByNamesRefusesAnUnsupportedTypeBeforeChangingAnything()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        var before = data.Scripts.Count;

        Assert.Throws<NotSupportedException>(() => ResourceImportService.Import(data, _root, new Dictionary<string, IReadOnlySet<string>>
        {
            ["Scripts"] = new HashSet<string> { "scr_x" },
            ["Options"] = new HashSet<string> { "x" },
        }));

        Assert.Equal(before, data.Scripts.Count);
    }
}
