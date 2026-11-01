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
}
