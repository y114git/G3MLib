using System.Diagnostics;
using System.Text.Json;
using G3MLib.Analyzer.Compiler;
using G3MLib.Analyzer.Decompiler;
using G3MLib.Analyzer.Mock;
using G3MLib.DataFile;
using G3MLib.DataFile.Decompiler;
using G3MLib.Modding.Caching;
using G3MLib.Modding.Diffing;
using G3MLib.Modding.Hashing;
using G3MLib.Modding.Logging;
using G3MLib.Modding.Merging;
using G3MLib.Modding.Patching;
using G3MLib.Modding.Resources;

bool compare = args.Contains("--compare");
bool full = args.Contains("--full") || compare;
string[] paths = args.Where(arg => arg != "--full" && arg != "--compare").ToArray();
if (paths.Length == 0)
{
    Console.Error.WriteLine("Usage: G3MLib.Benchmarks [--full | --compare] <data-file> [data-file ...]");
    return 1;
}
using var logScope = LogService.BeginScope(null, null, suppress: true);
foreach (string inputPath in paths)
{
    string path = Path.GetFullPath(inputPath);
    using var data = Read(path);
    Console.WriteLine(JsonSerializer.Serialize(new { file = Path.GetFileName(path), bytes = new FileInfo(path).Length, runs = compare ? 7 : full ? 3 : 5 }));
    if (compare)
    {
#if UPSTREAM
        UpstreamComparison.Run(path, data);
        continue;
#else
        throw new InvalidOperationException("Build with -p:UndertaleModToolPath=<source-directory> to compare libraries.");
#endif
    }
    Measure("read", () => { using var parsed = Read(path); });
    Measure("read_general", () => { using var stream = File.OpenRead(path); using var parsed = GameMakerIO.Read(stream, onlyGeneralInfo: true); });
    Measure("write", () => { using var output = new MemoryStream(); GameMakerIO.Write(output, data); });
    Measure("hash", () => ResourceHashService.HashAll(data, path));
    Measure("md5", () => FileHash.ComputeMd5(path));
    await MeasureAsync("md5_async", async () => { await FileHash.ComputeMd5Async(path); });
    if (!full) continue;

    string source = string.Join('\n', Enumerable.Range(0, 200).Select(i => $"var value{i} = {i} + 1;"));
    Measure("parse_gml", () =>
    {
        var context = new CompileContext(source, CompileScriptKind.ObjectEvent, null, new GameContextMock());
        context.Parse();
        Check(!context.HasErrors, "GML parsing failed");
    });
    Measure("compile_gml", () =>
    {
        var context = new CompileContext(source, CompileScriptKind.ObjectEvent, null, new GameContextMock());
        context.Compile();
        Check(!context.HasErrors, "GML compilation failed");
    });
    if (data.Code.Count > 0)
    {
        var largest = data.Code.OrderByDescending(code => code.Instructions.Count).First();
        Console.WriteLine(JsonSerializer.Serialize(new { largestCodeInstructions = largest.Instructions.Count }));
        Measure("decompile", () => new DecompileContext(new GlobalDecompileContext(data), largest).DecompileToString());
    }
    Measure("info_snapshot", () => G3MCacheService.BuildInfoSnapshot(path, data));

    string directory = Path.Combine(Path.GetTempPath(), $"G3MLibBenchmarks-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        string Output(string name) => Path.Combine(directory, name);
        var hashes = ResourceHashService.HashAll(data, path);
        var info = new DataFileInfo { Filename = Path.GetFileName(path), Size = new FileInfo(path).Length, Md5 = FileHash.ComputeMd5(path), BytecodeVersion = data.GeneralInfo.BytecodeVersion };
        var names = PatchService.GetResourceNameCountsForReuse(data);
        var order = PatchService.GetOrderSensitiveResourceNamesForReuse(data);
        var snapshot = G3MCacheService.BuildInfoSnapshot(path, data);
        var cache = G3MCacheOptions.FromDirectory(Output("cache"));
        await MeasureAsync("cache_write", async () =>
        {
            await G3MCacheService.WriteDataCacheAsync(path, info, hashes, names, order, cache, snapshot);
            Check(Directory.GetFiles(Output("cache"), "*.g3mcache").Length > 0, "Cache was not written");
        });
        Measure("cache_read", () => Check(G3MCacheService.TryReadDataCache(path, cache) != null, "Cache could not be read"));
        Measure("export_sprites", () => ResourceExportService.ExportSprites(data, Output("sprites")));
        int expectedFrames = data.Sprites.Sum(sprite => sprite.Textures.Count(frame => frame.Texture != null));
        int exportedFrames = Directory.Exists(Output("sprites"))
            ? Directory.GetFiles(Output("sprites"), "*.png", SearchOption.AllDirectories).Length : 0;
        Check(exportedFrames == expectedFrames, "Sprite frame export is incomplete");
        Console.WriteLine(JsonSerializer.Serialize(new { exportedFrames }));
        if (data.Rooms.Count < 8) throw new InvalidOperationException("Full patch benchmarks require at least eight rooms.");
        var patches = new List<string>();
        for (int i = 0; i < 8; i++)
        {
            using var modified = Read(path);
            modified.Rooms[i].Width++;
            string modifiedPath = Output($"modified-{i}.win");
            PatchInputService.WriteDataFile(modified, modifiedPath, path);
            string patch = Output($"patch-{i}.g3mpatch");
            var created = await PatchService.CreatePatchAsync(path, modifiedPath, patch);
            Check(created.Success, created.Error);
            patches.Add(patch);
        }
        await MeasureAsync("patch_validate", async () => { var result = await PatchService.ValidatePatchAsync(patches[0], path); Check(result.Success, result.Error); });
        await MeasureAsync("patch_create", async () => { var result = await PatchService.CreatePatchAsync(path, Output("modified-0.win"), Output("created.g3mpatch")); Check(result.Success, result.Error); });
        await MeasureAsync("patch_apply", async () => { var result = await PatchService.ApplyPatchAsync(path, patches[0], Output("applied.win")); Check(result.Success, result.Error); });
        foreach (var mode in new[] { DiffReportMode.Standard, DiffReportMode.Full })
            await MeasureAsync($"diff_{mode.ToString().ToLowerInvariant()}", async () => { var result = await DiffService.CompareAsync(path, Output("modified-0.win"), Output($"diff-{mode}.md"), mode); Check(result.Success, result.Error); });
        foreach (int count in new[] { 2, 4, 8 })
            await MeasureAsync($"merge_{count}", async () => { var result = await MergeService.MergePatchesAsync(path, patches.Take(count).ToList(), new MergeOptions { OutputPath = Output($"merged-{count}.g3mpatch") }); Check(result.Success, result.Error); });
        await MeasureAsync("merge_apply_8", async () => { var result = await MergeService.MergePatchesAsync(path, patches, new MergeOptions { OutputPath = Output("merged.g3mpatch"), ApplyPath = Output("merged.win") }); Check(result.Success, result.Error); });
        using var applied = Read(Output("applied.win"));
        Check(applied.Rooms[0].Width == data.Rooms[0].Width + 1, "Applied room width differs");
        using var merged = Read(Output("merged.win"));
        for (int i = 0; i < 8; i++) Check(merged.Rooms[i].Width == data.Rooms[i].Width + 1, "Merged room width differs");
        Console.WriteLine(JsonSerializer.Serialize(new { validation = "patch and merge room changes match" }));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}
return 0;

static GameMakerData Read(string path)
{
    using var stream = File.OpenRead(path);
    return GameMakerIO.Read(stream);
}
static void Check(bool success, string? error)
{
    if (!success) throw new InvalidOperationException(error ?? "Operation failed");
}
void Measure(string operation, Action action) => MeasureAsync(operation, () => { action(); return Task.CompletedTask; }).GetAwaiter().GetResult();
async Task MeasureAsync(string operation, Func<Task> action)
{
    await action();
    var times = new double[full ? 3 : 5];
    var allocations = new long[times.Length];
    for (int i = 0; i < times.Length; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var timer = Stopwatch.StartNew();
        await action();
        timer.Stop();
        times[i] = timer.Elapsed.TotalMilliseconds;
        allocations[i] = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
    }
    Array.Sort(times);
    Array.Sort(allocations);
    Console.WriteLine(JsonSerializer.Serialize(new { operation, medianMs = times[times.Length / 2], minMs = times[0], maxMs = times[^1], allocatedMiB = allocations[allocations.Length / 2] / 1048576d }));
}
