using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.Modding.Diffing;
using G3MLib.Modding.Logging;
using G3MLib.Modding.Merging;
using G3MLib.Modding.Patching;
using G3MLib.Modding.Resources;

namespace G3MLib.Tests;

[CollectionDefinition(nameof(ModdingCancellationTests), DisableParallelization = true)]
public sealed class ModdingCancellationCollection;

[Collection(nameof(ModdingCancellationTests))]
public sealed class ModdingCancellationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"G3MLibTests-{Guid.NewGuid():N}");
    private string Original => Path.Combine(_dir, "original.win");
    private string Modified => Path.Combine(_dir, "modified.win");
    private string Patch => Path.Combine(_dir, "change.g3mpatch");

    private readonly string _tempDir;

    // The library temp root is redirected for this async flow only, so work of other test classes never lands here.
    public ModdingCancellationTests()
    {
        _tempDir = Path.Combine(_dir, "temp");
        Directory.CreateDirectory(_tempDir);
        TempRoot.Redirect(_tempDir);
    }

    public void Dispose()
    {
        TempRoot.Redirect(null);
        Directory.Delete(_dir, recursive: true);
    }

    // The seq folders are shared parents the library keeps between runs; only what is inside them counts.
    private string[] TempEntries() => [.. Directory.GetFileSystemEntries(_tempDir, "*", SearchOption.AllDirectories)
        .Where(path => Path.GetFileName(path) is not ("g3mlib_seq_hash" or "g3mlib_seq_import"))
        .Select(path => Path.GetRelativePath(_tempDir, path)).Order()];

    private static CancellationToken Cancelled()
    {
        CancellationTokenSource source = new();
        source.Cancel();
        return source.Token;
    }

    /// <summary>Runs the operation with a token that is cancelled by the first progress report at or past <paramref name="percent"/>.</summary>
    private static async Task CancelFromProgress(int percent, Func<CancellationToken, Task> operation)
    {
        using CancellationTokenSource source = new();
        using var scope = LogService.BeginScope(null, progress =>
        {
            if (progress.Total > 0 && progress.Current * 100 >= percent * progress.Total) source.Cancel();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation(source.Token));
        Assert.True(source.IsCancellationRequested);
    }

    private void CreateInputs()
    {
        using var data = GameMakerData.CreateNew2026LTS();
        Write(Original, data);
        data.Rooms[0].Width++;
        Write(Modified, data);
    }

    private async Task CreatePatchFile() => Assert.True((await PatchService.CreatePatchAsync(Original, Modified, Patch, cancellationToken: TestContext.Current.CancellationToken)).Success);

    private static void Write(string path, GameMakerData data)
    {
        using var stream = File.Create(path);
        GameMakerIO.Write(stream, data);
    }

    private string[] Snapshot() => [.. Directory.GetFileSystemEntries(_dir, "*", SearchOption.AllDirectories).Where(path => !path.StartsWith(_tempDir, StringComparison.Ordinal)).Order()];

    private async Task AssertCleanAfter(Func<Task> operation)
    {
        var files = Snapshot();
        var temp = TempEntries();
        await operation();
        Assert.Equal(files, Snapshot());
        Assert.Equal(temp, TempEntries());
    }

    [Fact]
    public async Task TempRootIsRedirectedInsideTests()
    {
        await Task.Yield();
        Assert.Equal(_tempDir, TempRoot.Path);
    }

    [Fact]
    public async Task CreatePatchCancelledEarlyLeavesNothing()
    {
        CreateInputs();
        await AssertCleanAfter(() => Assert.ThrowsAnyAsync<OperationCanceledException>(() => PatchService.CreatePatchAsync(Original, Modified, Patch, cancellationToken: Cancelled())));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(60)]
    public async Task CreatePatchCancelledFromProgressLeavesNothing(int percent)
    {
        CreateInputs();
        await AssertCleanAfter(() => CancelFromProgress(percent, token => PatchService.CreatePatchAsync(Original, Modified, Patch, cancellationToken: token)));
    }

    [Fact]
    public async Task ApplyPatchCancelledEarlyKeepsExistingOutput()
    {
        CreateInputs();
        await CreatePatchFile();
        var output = Path.Combine(_dir, "output.win");
        await File.WriteAllTextAsync(output, "previous", TestContext.Current.CancellationToken);
        await AssertCleanAfter(() => Assert.ThrowsAnyAsync<OperationCanceledException>(() => PatchService.ApplyPatchAsync(Original, Patch, output, cancellationToken: Cancelled())));
        Assert.Equal("previous", await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(100)]
    public async Task ApplyPatchCancelledFromProgressKeepsExistingOutput(int percent)
    {
        CreateInputs();
        await CreatePatchFile();
        var output = Path.Combine(_dir, "output.win");
        await File.WriteAllTextAsync(output, "previous", TestContext.Current.CancellationToken);
        await AssertCleanAfter(() => CancelFromProgress(percent, token => PatchService.ApplyPatchAsync(Original, Patch, output, cancellationToken: token)));
        Assert.Equal("previous", await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ValidatePatchCancelledEarlyThrows()
    {
        CreateInputs();
        await CreatePatchFile();
        await AssertCleanAfter(() => Assert.ThrowsAnyAsync<OperationCanceledException>(() => PatchService.ValidatePatchAsync(Patch, Original, cancellationToken: Cancelled())));
    }

    [Fact]
    public async Task ValidatePatchCancelledFromLogThrows()
    {
        CreateInputs();
        await CreatePatchFile();
        var other = Path.Combine(_dir, "other.win");
        using (var data = GameMakerData.CreateNew2026LTS())
        {
            data.Rooms[0].Width += 5;
            Write(other, data);
        }
        using CancellationTokenSource source = new();
        using var scope = LogService.BeginScope(message => source.Cancel(), null);
        await AssertCleanAfter(() => Assert.ThrowsAnyAsync<OperationCanceledException>(() => PatchService.ValidatePatchAsync(Patch, other, cancellationToken: source.Token)));
        Assert.True(source.IsCancellationRequested);
    }

    private async Task<(string Patch, string Applied)> PreparePreviousMergeOutputs()
    {
        CreateInputs();
        await CreatePatchFile();
        var merged = Path.Combine(_dir, "merged.g3mpatch");
        var applied = Path.Combine(_dir, "merged.win");
        await File.WriteAllTextAsync(merged, "previous", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(applied, "previous", TestContext.Current.CancellationToken);
        return (merged, applied);
    }

    private static async Task AssertMergeOutputsKept((string Patch, string Applied) outputs)
    {
        Assert.Equal("previous", await File.ReadAllTextAsync(outputs.Patch, TestContext.Current.CancellationToken));
        Assert.Equal("previous", await File.ReadAllTextAsync(outputs.Applied, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MergeCancelledEarlyKeepsExistingOutputs(bool sequential)
    {
        var outputs = await PreparePreviousMergeOutputs();
        await AssertCleanAfter(() => Assert.ThrowsAnyAsync<OperationCanceledException>(() => MergeService.MergePatchesAsync(Original, [Patch, Patch], new MergeOptions { OutputPath = outputs.Patch, ApplyPath = outputs.Applied, UseSequentialMerge = sequential }, Cancelled())));
        await AssertMergeOutputsKept(outputs);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 20)]
    [InlineData(false, 70)]
    [InlineData(false, 80)]
    [InlineData(false, 90)]
    [InlineData(false, 98)]
    [InlineData(true, 3)]
    [InlineData(true, 20)]
    [InlineData(true, 60)]
    [InlineData(true, 80)]
    public async Task MergeCancelledFromProgressKeepsExistingOutputs(bool sequential, int percent)
    {
        var outputs = await PreparePreviousMergeOutputs();
        await AssertCleanAfter(() => CancelFromProgress(percent, token => MergeService.MergePatchesAsync(Original, [Patch, Patch], new MergeOptions { OutputPath = outputs.Patch, ApplyPath = outputs.Applied, UseSequentialMerge = sequential }, token)));
        await AssertMergeOutputsKept(outputs);
    }

    private async Task<(string First, string Second, string Report)> PrepareDiff(string kind)
    {
        CreateInputs();
        await CreatePatchFile();
        var report = Path.Combine(_dir, "report.md");
        await File.WriteAllTextAsync(report, "previous", TestContext.Current.CancellationToken);
        return kind switch
        {
            "data" => (Original, Modified, report),
            "patch-data" => (Patch, Modified, report),
            _ => (Patch, Patch, report)
        };
    }

    [Theory]
    [InlineData("data")]
    [InlineData("patch-data")]
    [InlineData("patches")]
    public async Task DiffCancelledEarlyKeepsExistingReport(string kind)
    {
        var (first, second, report) = await PrepareDiff(kind);
        await AssertCleanAfter(() => Assert.ThrowsAnyAsync<OperationCanceledException>(() => DiffService.CompareAsync(first, second, report, cancellationToken: Cancelled())));
        Assert.Equal("previous", await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("data", 0)]
    [InlineData("data", 10)]
    [InlineData("data", 50)]
    [InlineData("data", 92)]
    [InlineData("patch-data", 10)]
    [InlineData("patch-data", 60)]
    [InlineData("patch-data", 90)]
    [InlineData("patches", 20)]
    [InlineData("patches", 95)]
    public async Task DiffCancelledFromProgressKeepsExistingReport(string kind, int percent)
    {
        var (first, second, report) = await PrepareDiff(kind);
        await AssertCleanAfter(() => CancelFromProgress(percent, token => DiffService.CompareAsync(first, second, report, cancellationToken: token)));
        Assert.Equal("previous", await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken));
    }

    private static GameMakerData DataWithResources()
    {
        var data = GameMakerData.CreateNew2026LTS();
        for (var i = 0; i < 3; i++)
        {
            data.GameObjects.Add(new GameMakerGameObject { Name = data.Strings.MakeString($"obj_{i}") });
            data.Rooms.Add(new GameMakerRoom { Name = data.Strings.MakeString($"room_{i}") });
        }
        return data;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExportTypesCancelledEarlyLeavesOutputUntouched(bool outputExists)
    {
        using var data = DataWithResources();
        var output = Path.Combine(_dir, "export");
        if (outputExists)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "keep.txt"), "keep");
        }
        var before = Snapshot();
        Assert.ThrowsAny<OperationCanceledException>(() => ResourceExportService.ExportTypes(data, output, Original, ["GameObjects", "Rooms"], Cancelled()));
        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExportTypesCancelledMidWayRemovesPartialOutput(bool outputExists)
    {
        using var data = DataWithResources();
        var output = Path.Combine(_dir, "export");
        if (outputExists)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "keep.txt"), "keep");
        }
        var before = Snapshot();
        using CancellationTokenSource source = new();
        IEnumerable<string> Types()
        {
            yield return "GameObjects";
            Assert.True(Directory.Exists(Path.Combine(output, "GameObjects")));
            source.Cancel();
            yield return "Rooms";
        }
        Assert.ThrowsAny<OperationCanceledException>(() => ResourceExportService.ExportTypes(data, output, Original, Types(), source.Token));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void ExportSelectiveCancelledFromLogRemovesPartialOutput()
    {
        using var data = DataWithResources();
        var output = Path.Combine(_dir, "export");
        var before = Snapshot();
        using CancellationTokenSource source = new();
        using var scope = LogService.BeginScope(message => source.Cancel(), null, verbose: true);
        Dictionary<string, HashSet<string>> changed = new() { ["GameObjects"] = ["obj_0"], ["Rooms"] = ["room_0"] };
        Assert.ThrowsAny<OperationCanceledException>(() => ResourceExportService.ExportSelectiveExceptCode(data, output, Original, changed, source.Token));
        Assert.True(source.IsCancellationRequested);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void ExportCodeEntriesCancelledEarlyThrows()
    {
        using var data = DataWithResources();
        var output = Path.Combine(_dir, "export");
        Assert.ThrowsAny<OperationCanceledException>(() => ResourceExportService.ExportCodeEntries(data, output, Cancelled()));
        Assert.ThrowsAny<OperationCanceledException>(() => ResourceExportService.ExportCodeEntriesSelective(data, output, ["a"], Cancelled()));
        Assert.False(Directory.Exists(output));
    }

    private string WriteTimelines()
    {
        var input = Path.Combine(_dir, "import");
        for (var i = 0; i < 3; i++)
        {
            Directory.CreateDirectory(Path.Combine(input, $"timeline_{i}"));
            File.WriteAllText(Path.Combine(input, $"timeline_{i}", $"timeline_{i}.json"), /*lang=json*/ "{\"moments\":[]}");
        }
        return input;
    }

    [Fact]
    public void ImportCancelledEarlyLeavesInputUntouched()
    {
        var input = WriteTimelines();
        var before = Snapshot();
        using var data = GameMakerData.CreateNew2026LTS();
        Assert.ThrowsAny<OperationCanceledException>(() => ResourceImportService.Import("Timelines", data, input, Cancelled()));
        Assert.ThrowsAny<OperationCanceledException>(() => ResourceImportService.ImportAssetOrder(data, input, Cancelled()));
        Assert.Equal(before, Snapshot());
        Assert.Empty(data.Timelines);
    }

    [Fact]
    public void ImportCancelledMidWayLeavesInputUntouched()
    {
        var input = WriteTimelines();
        File.WriteAllText(Path.Combine(input, "timeline_1", "timeline_1.json"), "not json");
        var before = Snapshot();
        using var data = GameMakerData.CreateNew2026LTS();
        using CancellationTokenSource source = new();
        using var scope = LogService.BeginScope(message => source.Cancel(), null, verbose: true);
        Assert.ThrowsAny<OperationCanceledException>(() => ResourceImportService.Import("Timelines", data, input, source.Token));
        Assert.True(source.IsCancellationRequested);
        Assert.Single(data.Timelines);
        Assert.Equal(before, Snapshot());
    }
}
